using BillingRecon.Application.Common;
using BillingRecon.Application.Receivables;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Receivables;

/// <summary>
/// The reconciliation of docs/database.md section 5.6 (BR-404): one read-only statement that returns a row for every
/// place where a cached balance of a payer differs from the ledger, or where the ledger breaks one of its own
/// identities, for the current tenant. No rows means no drift. The identities of each document join it with the
/// slice that posts the document: a bill's balance due against its receivable, and a posted bill against its
/// posting, since Prompt 3.5 (a bill that came to nothing has no posting, ADR 0022); a payment's unapplied amount
/// against its unapplied cash, and a payment against its posting, since Prompt 4.1; a payment against the sum of its
/// allocations, an allocation against its posting, and the payers of an allocation's two documents, since Prompt
/// 4.2; a credit note's available amount against its available credit and against its applications, a credit note
/// and an application against their postings, the payers of an application's two documents, and the credits of a
/// bill against its total (BR-210), since Prompt 4.3; a posted adjustment against its posting, and its payer against
/// the payer of its bill, since Prompt 4.4; a reversed allocation and a reversed payment against their mirror
/// postings, both directions, since Prompt 4.5, which also teaches the identity of a payment's allocations that an
/// allocation waiting for its reversal still counts and that a reversed payment holds nothing; a reversed application
/// of credit against its mirror posting, both directions, since Prompt 4.5b, which also teaches the identity of a
/// credit note's applications that an application waiting for its reversal still counts; a posted refund against its
/// posting, both directions, and its payer against the payer of its source, since Prompt 4.6, which also takes the
/// posted refunds of a payment or a credit note off what the identities of their allocations and applications expect;
/// and every number series against the documents numbered from it (identity 11, gapless: as many documents as the
/// highest number, which is the number before the series' next one), since Prompt 6.2.
/// <para>
/// This is one of the files of the production code that send SQL of their own (ADR 0021, BannedSymbolsTests). It is
/// the SQL the design document gives: sums over the whole ledger, grouped, which Entity Framework cannot translate
/// for columns stored through a converter, and from Prompt 3.5 on a comparison with the tables of other modules,
/// which a query through the entities of one module may not make (ADR 0003). It reads and never writes; the tenant
/// is a parameter taken from the current scope and is in every WHERE clause; the text is a constant.
/// </para>
/// </summary>
public sealed class ReconciliationQuery(BillingReconDbContext context, ITenantContext tenantContext) : IReconciliationQuery
{
    private const string FindDrift =
        """
        WITH [PayerLedger] AS (
            SELECT [PayerId],
                   SUM(CASE WHEN [Account] = 'Receivable'      THEN [Debit] - [Credit] ELSE 0 END) AS [Receivable],
                   SUM(CASE WHEN [Account] = 'UnappliedCash'   THEN [Credit] - [Debit] ELSE 0 END) AS [UnappliedCash],
                   SUM(CASE WHEN [Account] = 'CreditAvailable' THEN [Credit] - [Debit] ELSE 0 END) AS [CreditAvailable]
            FROM   [dbo].[LedgerEntries]
            WHERE  [TenantId] = @tenant AND [PayerId] IS NOT NULL
            GROUP  BY [PayerId]
        ),
        [BillLedger] AS (
            SELECT [SubjectId], SUM([Debit] - [Credit]) AS [Balance]
            FROM   [dbo].[LedgerEntries]
            WHERE  [TenantId] = @tenant AND [Account] = 'Receivable'
            GROUP  BY [SubjectId]
        ),
        [PaymentLedger] AS (
            SELECT [SubjectId], SUM([Credit] - [Debit]) AS [Balance]
            FROM   [dbo].[LedgerEntries]
            WHERE  [TenantId] = @tenant AND [Account] = 'UnappliedCash'
            GROUP  BY [SubjectId]
        ),
        [CreditNoteLedger] AS (
            SELECT [SubjectId], SUM([Credit] - [Debit]) AS [Balance]
            FROM   [dbo].[LedgerEntries]
            WHERE  [TenantId] = @tenant AND [Account] = 'CreditAvailable'
            GROUP  BY [SubjectId]
        ),
        [PostedRefunds] AS (
            SELECT ISNULL([SourcePaymentId], [SourceCreditNoteId]) AS [SourceId], SUM([Amount]) AS [Refunded]
            FROM   [dbo].[Refunds]
            WHERE  [TenantId] = @tenant AND [Status] = 'Posted'
            GROUP  BY ISNULL([SourcePaymentId], [SourceCreditNoteId])
        ),
        [NumberedDocuments] AS (
            SELECT 'Bill' AS [DocumentType], [FiscalYear], COUNT(*) AS [Documents], MAX([SequenceNumber]) AS [Highest]
            FROM   [dbo].[Bills] WHERE [TenantId] = @tenant AND [SequenceNumber] IS NOT NULL GROUP BY [FiscalYear]
            UNION ALL
            SELECT 'CreditNote', [FiscalYear], COUNT(*), MAX([SequenceNumber])
            FROM   [dbo].[CreditNotes] WHERE [TenantId] = @tenant AND [SequenceNumber] IS NOT NULL GROUP BY [FiscalYear]
            UNION ALL
            SELECT 'Payment', [FiscalYear], COUNT(*), MAX([SequenceNumber])
            FROM   [dbo].[Payments] WHERE [TenantId] = @tenant GROUP BY [FiscalYear]
            UNION ALL
            SELECT 'Adjustment', [FiscalYear], COUNT(*), MAX([SequenceNumber])
            FROM   [dbo].[Adjustments] WHERE [TenantId] = @tenant AND [SequenceNumber] IS NOT NULL GROUP BY [FiscalYear]
            UNION ALL
            SELECT 'Refund', [FiscalYear], COUNT(*), MAX([SequenceNumber])
            FROM   [dbo].[Refunds] WHERE [TenantId] = @tenant AND [SequenceNumber] IS NOT NULL GROUP BY [FiscalYear]
        )
        SELECT CAST('UnbalancedPosting' AS varchar(30)) AS [Identity], e.[PostingId] AS [SubjectId], CAST(NULL AS varchar(30)) AS [Reference],
               SUM(e.[Debit]) AS [Cached], SUM(e.[Credit]) AS [Ledger]
        FROM   [dbo].[LedgerEntries] AS e
        WHERE  e.[TenantId] = @tenant
        GROUP  BY e.[PostingId]
        HAVING SUM(e.[Debit]) <> SUM(e.[Credit]) OR COUNT(*) < 2

        UNION ALL
        SELECT 'UnbalancedPosting', p.[Id], NULL, 0, 0
        FROM   [dbo].[LedgerPostings] AS p
        WHERE  p.[TenantId] = @tenant
          AND  NOT EXISTS (SELECT 1 FROM [dbo].[LedgerEntries] AS e WHERE e.[TenantId] = @tenant AND e.[PostingId] = p.[Id])

        UNION ALL
        SELECT 'BillBalance', b.[Id], b.[Number], b.[BalanceDue], ISNULL(l.[Balance], 0)
        FROM   [dbo].[Bills] AS b
        LEFT JOIN [BillLedger] AS l ON l.[SubjectId] = b.[Id]
        WHERE  b.[TenantId] = @tenant AND b.[Number] IS NOT NULL AND b.[BalanceDue] <> ISNULL(l.[Balance], 0)

        UNION ALL
        SELECT 'BillWithoutPosting', b.[Id], b.[Number], b.[Total], 0
        FROM   [dbo].[Bills] AS b
        WHERE  b.[TenantId] = @tenant AND b.[Number] IS NOT NULL AND b.[Total] > 0
          AND  NOT EXISTS (SELECT 1 FROM [dbo].[LedgerPostings] AS p
                           WHERE p.[TenantId] = @tenant AND p.[DocumentType] = 'Bill' AND p.[DocumentId] = b.[Id] AND p.[Kind] = 'BillPosted')

        UNION ALL
        SELECT 'PostingWithoutBill', p.[Id], NULL, 0, 0
        FROM   [dbo].[LedgerPostings] AS p
        WHERE  p.[TenantId] = @tenant AND p.[Kind] = 'BillPosted'
          AND  NOT EXISTS (SELECT 1 FROM [dbo].[Bills] AS b WHERE b.[TenantId] = @tenant AND b.[Id] = p.[DocumentId] AND b.[Number] IS NOT NULL)

        UNION ALL
        SELECT 'PaymentUnapplied', p.[Id], p.[Number], p.[UnappliedAmount], ISNULL(l.[Balance], 0)
        FROM   [dbo].[Payments] AS p
        LEFT JOIN [PaymentLedger] AS l ON l.[SubjectId] = p.[Id]
        WHERE  p.[TenantId] = @tenant AND p.[UnappliedAmount] <> ISNULL(l.[Balance], 0)

        UNION ALL
        SELECT 'PaymentWithoutPosting', p.[Id], p.[Number], p.[Amount], 0
        FROM   [dbo].[Payments] AS p
        WHERE  p.[TenantId] = @tenant
          AND  NOT EXISTS (SELECT 1 FROM [dbo].[LedgerPostings] AS lp
                           WHERE lp.[TenantId] = @tenant AND lp.[DocumentType] = 'Payment' AND lp.[DocumentId] = p.[Id] AND lp.[Kind] = 'PaymentRecorded')

        UNION ALL
        SELECT 'PostingWithoutPayment', lp.[Id], NULL, 0, 0
        FROM   [dbo].[LedgerPostings] AS lp
        WHERE  lp.[TenantId] = @tenant AND lp.[Kind] = 'PaymentRecorded'
          AND  NOT EXISTS (SELECT 1 FROM [dbo].[Payments] AS p WHERE p.[TenantId] = @tenant AND p.[Id] = lp.[DocumentId])

        UNION ALL
        SELECT 'PaymentAllocations', p.[Id], p.[Number], p.[UnappliedAmount],
               CASE WHEN p.[Status] = 'Reversed' THEN 0 ELSE p.[Amount] - ISNULL(a.[Allocated], 0) - ISNULL(r.[Refunded], 0) END
        FROM   [dbo].[Payments] AS p
        LEFT JOIN (SELECT [PaymentId], SUM([Amount]) AS [Allocated]
                   FROM   [dbo].[PaymentAllocations]
                   WHERE  [TenantId] = @tenant AND [Status] <> 'Reversed'
                   GROUP  BY [PaymentId]) AS a ON a.[PaymentId] = p.[Id]
        LEFT JOIN [PostedRefunds] AS r ON r.[SourceId] = p.[Id]
        WHERE  p.[TenantId] = @tenant
          AND  p.[UnappliedAmount] <> CASE WHEN p.[Status] = 'Reversed' THEN 0 ELSE p.[Amount] - ISNULL(a.[Allocated], 0) - ISNULL(r.[Refunded], 0) END

        UNION ALL
        SELECT 'AllocationWithoutPosting', a.[Id], NULL, a.[Amount], 0
        FROM   [dbo].[PaymentAllocations] AS a
        WHERE  a.[TenantId] = @tenant
          AND  NOT EXISTS (SELECT 1 FROM [dbo].[LedgerPostings] AS lp
                           WHERE lp.[TenantId] = @tenant AND lp.[DocumentType] = 'PaymentAllocation' AND lp.[DocumentId] = a.[Id] AND lp.[Kind] = 'PaymentAllocated')

        UNION ALL
        SELECT 'PostingWithoutAllocation', lp.[Id], NULL, 0, 0
        FROM   [dbo].[LedgerPostings] AS lp
        WHERE  lp.[TenantId] = @tenant AND lp.[Kind] = 'PaymentAllocated'
          AND  NOT EXISTS (SELECT 1 FROM [dbo].[PaymentAllocations] AS a WHERE a.[TenantId] = @tenant AND a.[Id] = lp.[DocumentId])

        UNION ALL
        SELECT 'AllocationPayerMismatch', a.[Id], NULL, a.[Amount], 0
        FROM   [dbo].[PaymentAllocations] AS a
        JOIN   [dbo].[Payments] AS p ON p.[Id] = a.[PaymentId]
        JOIN   [dbo].[Bills] AS b ON b.[Id] = a.[BillId]
        WHERE  a.[TenantId] = @tenant AND p.[PayerId] <> b.[PayerId]

        UNION ALL
        SELECT 'ReversedAllocationNoPosting', a.[Id], NULL, a.[Amount], 0
        FROM   [dbo].[PaymentAllocations] AS a
        WHERE  a.[TenantId] = @tenant AND a.[Status] = 'Reversed'
          AND  NOT EXISTS (SELECT 1 FROM [dbo].[LedgerPostings] AS lp
                           WHERE lp.[TenantId] = @tenant AND lp.[DocumentType] = 'PaymentAllocation' AND lp.[DocumentId] = a.[Id]
                             AND lp.[Kind] = 'AllocationReversed' AND lp.[ReversalOfPostingId] = a.[PostingId])

        UNION ALL
        SELECT 'ReversedPaymentNoPosting', p.[Id], p.[Number], p.[Amount], 0
        FROM   [dbo].[Payments] AS p
        WHERE  p.[TenantId] = @tenant AND p.[Status] = 'Reversed'
          AND  NOT EXISTS (SELECT 1 FROM [dbo].[LedgerPostings] AS lp
                           WHERE lp.[TenantId] = @tenant AND lp.[DocumentType] = 'Payment' AND lp.[DocumentId] = p.[Id]
                             AND lp.[Kind] = 'PaymentReversed' AND lp.[ReversalOfPostingId] = p.[PostingId])

        UNION ALL
        SELECT 'ReversalPostingNoDocument', lp.[Id], NULL, 0, 0
        FROM   [dbo].[LedgerPostings] AS lp
        WHERE  lp.[TenantId] = @tenant
          AND  ((lp.[Kind] = 'AllocationReversed'
                 AND NOT EXISTS (SELECT 1 FROM [dbo].[PaymentAllocations] AS a
                                 WHERE a.[TenantId] = @tenant AND a.[Id] = lp.[DocumentId] AND a.[Status] = 'Reversed'))
             OR (lp.[Kind] = 'PaymentReversed'
                 AND NOT EXISTS (SELECT 1 FROM [dbo].[Payments] AS p
                                 WHERE p.[TenantId] = @tenant AND p.[Id] = lp.[DocumentId] AND p.[Status] = 'Reversed'))
             OR (lp.[Kind] = 'CreditApplicationReversed'
                 AND NOT EXISTS (SELECT 1 FROM [dbo].[CreditNoteAllocations] AS a
                                 WHERE a.[TenantId] = @tenant AND a.[Id] = lp.[DocumentId] AND a.[Status] = 'Reversed')))

        UNION ALL
        SELECT 'ReversedApplicationNoPosting', a.[Id], NULL, a.[Amount], 0
        FROM   [dbo].[CreditNoteAllocations] AS a
        WHERE  a.[TenantId] = @tenant AND a.[Status] = 'Reversed'
          AND  NOT EXISTS (SELECT 1 FROM [dbo].[LedgerPostings] AS lp
                           WHERE lp.[TenantId] = @tenant AND lp.[DocumentType] = 'CreditNoteAllocation' AND lp.[DocumentId] = a.[Id]
                             AND lp.[Kind] = 'CreditApplicationReversed' AND lp.[ReversalOfPostingId] = a.[PostingId])

        UNION ALL
        SELECT 'CreditNoteAvailable', c.[Id], c.[Number], c.[AvailableAmount], ISNULL(l.[Balance], 0)
        FROM   [dbo].[CreditNotes] AS c
        LEFT JOIN [CreditNoteLedger] AS l ON l.[SubjectId] = c.[Id]
        WHERE  c.[TenantId] = @tenant AND c.[Number] IS NOT NULL AND c.[AvailableAmount] <> ISNULL(l.[Balance], 0)

        UNION ALL
        SELECT 'CreditNoteWithoutPosting', c.[Id], c.[Number], c.[Total], 0
        FROM   [dbo].[CreditNotes] AS c
        WHERE  c.[TenantId] = @tenant AND c.[Number] IS NOT NULL
          AND  NOT EXISTS (SELECT 1 FROM [dbo].[LedgerPostings] AS lp
                           WHERE lp.[TenantId] = @tenant AND lp.[DocumentType] = 'CreditNote' AND lp.[DocumentId] = c.[Id] AND lp.[Kind] = 'CreditNotePosted')

        UNION ALL
        SELECT 'PostingWithoutCreditNote', lp.[Id], NULL, 0, 0
        FROM   [dbo].[LedgerPostings] AS lp
        WHERE  lp.[TenantId] = @tenant AND lp.[Kind] = 'CreditNotePosted'
          AND  NOT EXISTS (SELECT 1 FROM [dbo].[CreditNotes] AS c WHERE c.[TenantId] = @tenant AND c.[Id] = lp.[DocumentId] AND c.[Number] IS NOT NULL)

        UNION ALL
        SELECT 'CreditNoteApplications', c.[Id], c.[Number], c.[AvailableAmount], c.[Total] - ISNULL(a.[Applied], 0) - ISNULL(r.[Refunded], 0)
        FROM   [dbo].[CreditNotes] AS c
        LEFT JOIN (SELECT [CreditNoteId], SUM([Amount]) AS [Applied]
                   FROM   [dbo].[CreditNoteAllocations]
                   WHERE  [TenantId] = @tenant AND [Status] <> 'Reversed'
                   GROUP  BY [CreditNoteId]) AS a ON a.[CreditNoteId] = c.[Id]
        LEFT JOIN [PostedRefunds] AS r ON r.[SourceId] = c.[Id]
        WHERE  c.[TenantId] = @tenant AND c.[Number] IS NOT NULL AND c.[AvailableAmount] <> c.[Total] - ISNULL(a.[Applied], 0) - ISNULL(r.[Refunded], 0)

        UNION ALL
        SELECT 'ApplicationWithoutPosting', a.[Id], NULL, a.[Amount], 0
        FROM   [dbo].[CreditNoteAllocations] AS a
        WHERE  a.[TenantId] = @tenant
          AND  NOT EXISTS (SELECT 1 FROM [dbo].[LedgerPostings] AS lp
                           WHERE lp.[TenantId] = @tenant AND lp.[DocumentType] = 'CreditNoteAllocation' AND lp.[DocumentId] = a.[Id] AND lp.[Kind] = 'CreditApplied')

        UNION ALL
        SELECT 'PostingWithoutApplication', lp.[Id], NULL, 0, 0
        FROM   [dbo].[LedgerPostings] AS lp
        WHERE  lp.[TenantId] = @tenant AND lp.[Kind] = 'CreditApplied'
          AND  NOT EXISTS (SELECT 1 FROM [dbo].[CreditNoteAllocations] AS a WHERE a.[TenantId] = @tenant AND a.[Id] = lp.[DocumentId])

        UNION ALL
        SELECT 'ApplicationPayerMismatch', a.[Id], NULL, a.[Amount], 0
        FROM   [dbo].[CreditNoteAllocations] AS a
        JOIN   [dbo].[CreditNotes] AS c ON c.[Id] = a.[CreditNoteId]
        JOIN   [dbo].[Bills] AS b ON b.[Id] = a.[BillId]
        WHERE  a.[TenantId] = @tenant AND c.[PayerId] <> b.[PayerId]

        UNION ALL
        SELECT 'CreditNotesExceedBillTotal', b.[Id], b.[Number], b.[Total], t.[Credited]
        FROM   [dbo].[Bills] AS b
        JOIN   (SELECT [BillId], SUM([Total]) AS [Credited]
                FROM   [dbo].[CreditNotes]
                WHERE  [TenantId] = @tenant AND [Status] = 'Posted'
                GROUP  BY [BillId]) AS t ON t.[BillId] = b.[Id]
        WHERE  b.[TenantId] = @tenant AND t.[Credited] > b.[Total]

        UNION ALL
        SELECT 'AdjustmentWithoutPosting', a.[Id], a.[Number], a.[Amount], 0
        FROM   [dbo].[Adjustments] AS a
        WHERE  a.[TenantId] = @tenant AND a.[Number] IS NOT NULL
          AND  NOT EXISTS (SELECT 1 FROM [dbo].[LedgerPostings] AS lp
                           WHERE lp.[TenantId] = @tenant AND lp.[DocumentType] = 'Adjustment' AND lp.[DocumentId] = a.[Id] AND lp.[Kind] = 'AdjustmentPosted')

        UNION ALL
        SELECT 'PostingWithoutAdjustment', lp.[Id], NULL, 0, 0
        FROM   [dbo].[LedgerPostings] AS lp
        WHERE  lp.[TenantId] = @tenant AND lp.[Kind] = 'AdjustmentPosted'
          AND  NOT EXISTS (SELECT 1 FROM [dbo].[Adjustments] AS a WHERE a.[TenantId] = @tenant AND a.[Id] = lp.[DocumentId] AND a.[Number] IS NOT NULL)

        UNION ALL
        SELECT 'AdjustmentPayerMismatch', a.[Id], a.[Number], a.[Amount], 0
        FROM   [dbo].[Adjustments] AS a
        JOIN   [dbo].[Bills] AS b ON b.[Id] = a.[BillId]
        WHERE  a.[TenantId] = @tenant AND a.[PayerId] <> b.[PayerId]

        UNION ALL
        SELECT 'RefundWithoutPosting', r.[Id], r.[Number], r.[Amount], 0
        FROM   [dbo].[Refunds] AS r
        WHERE  r.[TenantId] = @tenant AND r.[Number] IS NOT NULL
          AND  NOT EXISTS (SELECT 1 FROM [dbo].[LedgerPostings] AS lp
                           WHERE lp.[TenantId] = @tenant AND lp.[DocumentType] = 'Refund' AND lp.[DocumentId] = r.[Id] AND lp.[Kind] = 'RefundPosted')

        UNION ALL
        SELECT 'PostingWithoutRefund', lp.[Id], NULL, 0, 0
        FROM   [dbo].[LedgerPostings] AS lp
        WHERE  lp.[TenantId] = @tenant AND lp.[Kind] = 'RefundPosted'
          AND  NOT EXISTS (SELECT 1 FROM [dbo].[Refunds] AS r WHERE r.[TenantId] = @tenant AND r.[Id] = lp.[DocumentId] AND r.[Number] IS NOT NULL)

        UNION ALL
        SELECT 'RefundPayerMismatch', r.[Id], r.[Number], r.[Amount], 0
        FROM   [dbo].[Refunds] AS r
        LEFT JOIN [dbo].[Payments] AS p ON p.[Id] = r.[SourcePaymentId]
        LEFT JOIN [dbo].[CreditNotes] AS c ON c.[Id] = r.[SourceCreditNoteId]
        WHERE  r.[TenantId] = @tenant AND r.[PayerId] <> ISNULL(p.[PayerId], c.[PayerId])

        UNION ALL
        SELECT 'PayerReceivable', pb.[PayerId], NULL, pb.[Receivable], ISNULL(l.[Receivable], 0)
        FROM   [dbo].[PayerBalances] AS pb
        LEFT JOIN [PayerLedger] AS l ON l.[PayerId] = pb.[PayerId]
        WHERE  pb.[TenantId] = @tenant AND pb.[Receivable] <> ISNULL(l.[Receivable], 0)

        UNION ALL
        SELECT 'PayerUnappliedCash', pb.[PayerId], NULL, pb.[UnappliedCash], ISNULL(l.[UnappliedCash], 0)
        FROM   [dbo].[PayerBalances] AS pb
        LEFT JOIN [PayerLedger] AS l ON l.[PayerId] = pb.[PayerId]
        WHERE  pb.[TenantId] = @tenant AND pb.[UnappliedCash] <> ISNULL(l.[UnappliedCash], 0)

        UNION ALL
        SELECT 'PayerCreditAvailable', pb.[PayerId], NULL, pb.[CreditAvailable], ISNULL(l.[CreditAvailable], 0)
        FROM   [dbo].[PayerBalances] AS pb
        LEFT JOIN [PayerLedger] AS l ON l.[PayerId] = pb.[PayerId]
        WHERE  pb.[TenantId] = @tenant AND pb.[CreditAvailable] <> ISNULL(l.[CreditAvailable], 0)

        UNION ALL
        SELECT 'PayerWithoutBalances', l.[PayerId], NULL, 0, l.[Receivable] - l.[UnappliedCash] - l.[CreditAvailable]
        FROM   [PayerLedger] AS l
        WHERE  NOT EXISTS (SELECT 1 FROM [dbo].[PayerBalances] AS pb WHERE pb.[TenantId] = @tenant AND pb.[PayerId] = l.[PayerId])

        UNION ALL
        SELECT 'NegativeSubjectBalance', e.[SubjectId], NULL, 0,
               SUM(CASE WHEN e.[Account] = 'Receivable' THEN e.[Debit] - e.[Credit] ELSE e.[Credit] - e.[Debit] END)
        FROM   [dbo].[LedgerEntries] AS e
        WHERE  e.[TenantId] = @tenant AND e.[SubjectId] IS NOT NULL
        GROUP  BY e.[Account], e.[SubjectId]
        HAVING SUM(CASE WHEN e.[Account] = 'Receivable' THEN e.[Debit] - e.[Credit] ELSE e.[Credit] - e.[Debit] END) < 0

        UNION ALL
        SELECT 'NumberSeriesGap', ns.[Id], CAST(CONCAT(ns.[DocumentType], ' ', ns.[FiscalYear]) AS varchar(30)),
               ISNULL(d.[Documents], 0), ns.[NextNumber] - 1
        FROM   [dbo].[NumberSeries] AS ns
        LEFT JOIN [NumberedDocuments] AS d ON d.[DocumentType] = ns.[DocumentType] AND d.[FiscalYear] = ns.[FiscalYear]
        WHERE  ns.[TenantId] = @tenant
          AND (ISNULL(d.[Documents], 0) <> ISNULL(d.[Highest], 0) OR ISNULL(d.[Highest], 0) <> ns.[NextNumber] - 1)
        """;

    /// <inheritdoc />
    public async Task<IReadOnlyList<DriftRow>> FindDriftAsync(CancellationToken ct)
    {
        var tenantId = tenantContext.TenantId
            ?? throw new InvalidOperationException("The reconciliation was asked for in a scope that has no tenant.");

#pragma warning disable RS0030 // Sanctioned raw SQL: the read-only reconciliation of the ledger with its caches (ADR 0005, ADR 0021).
        var found = await context.Database.SqlQueryRaw<Found>(FindDrift, new SqlParameter("@tenant", tenantId.Value)).ToListAsync(ct);
#pragma warning restore RS0030

        return [.. found.Select(row => new DriftRow(row.Identity, row.SubjectId, row.Reference, row.Cached, row.Ledger))];
    }

    // What the statement returns.
    private sealed record Found(string Identity, Guid SubjectId, string? Reference, decimal Cached, decimal Ledger);
}
