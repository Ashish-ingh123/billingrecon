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
/// 4.2; then credit notes.
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
        SELECT 'PaymentAllocations', p.[Id], p.[Number], p.[UnappliedAmount], p.[Amount] - ISNULL(a.[Allocated], 0)
        FROM   [dbo].[Payments] AS p
        LEFT JOIN (SELECT [PaymentId], SUM([Amount]) AS [Allocated]
                   FROM   [dbo].[PaymentAllocations]
                   WHERE  [TenantId] = @tenant AND [Status] = 'Active'
                   GROUP  BY [PaymentId]) AS a ON a.[PaymentId] = p.[Id]
        WHERE  p.[TenantId] = @tenant AND p.[UnappliedAmount] <> p.[Amount] - ISNULL(a.[Allocated], 0)

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
