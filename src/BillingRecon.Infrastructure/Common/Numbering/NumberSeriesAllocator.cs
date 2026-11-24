using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Common.Numbering;

/// <summary>
/// Takes the next number of a series (BR-801, BR-802) with the statement of docs/database.md section 4.1, on the
/// connection and in the transaction of the request's context (ADR 0008):
/// <list type="bullet">
/// <item><c>UPDLOCK</c> makes two postings of one series wait for each other, so no two read the same number;</item>
/// <item><c>HOLDLOCK</c> holds the key range when the row does not exist yet, so two first postings of a fiscal year
/// cannot both create it;</item>
/// <item>the locks last until the transaction ends, and a rollback restores the row, so a posting that fails takes
/// no number with it.</item>
/// </list>
/// The transaction is opened here when the use case has none yet; the unit of work commits it with the save.
/// <para>
/// It also holds a series for an approved prefix change (<see cref="HoldAsync"/>, ADR 0033 built note point 5): the
/// same locked read, without the insert and the increment.
/// </para>
/// <para>
/// This file is one of the few in the production code that send SQL of their own: raw SQL goes around the tenant
/// filter, the save guards and the audit stamps, so its entry points are banned (RS0030) and only the sanctioned files
/// may use one (BannedSymbolsTests). Its two statements carry what those guards would have added: the tenant in every
/// WHERE clause, taken from the current scope, and the instants from the clock. Every value is a parameter; the texts
/// are constants.
/// </para>
/// <para>
/// Each statement is timed (<see cref="NumberSeriesTimer"/>, ADR 0046): how long it waited for the series, and from its
/// answer on, how long the series is held, until whoever ends the transaction says so.
/// </para>
/// </summary>
public sealed class NumberSeriesAllocator(
    BillingReconDbContext context,
    ITenantContext tenantContext,
    IClock clock,
    NumberSeriesTimer? timer = null) : INumberSeries
{
    // The read of TakeNext alone: the row of the series, or the gap in the index where it will go, locked until the
    // transaction ends. COUNT(*) only gives the statement a result; nobody reads it.
    private const string HoldSeries =
        """
        SELECT COUNT(*) AS [Value]
        FROM   [dbo].[NumberSeries] WITH (UPDLOCK, HOLDLOCK)
        WHERE  [TenantId] = @tenant AND [DocumentType] = @type AND [FiscalYear] = @fiscalYear;
        """;

    // The tenant's default prefix for the document type is read only when the series is created, under the same
    // range lock. Without a tenant row there is no prefix: nothing is inserted and no number comes back.
    private const string TakeNext =
        """
        SET NOCOUNT ON;

        DECLARE @next int, @prefix varchar(3);

        SELECT @next = [NextNumber], @prefix = [Prefix]
        FROM   [dbo].[NumberSeries] WITH (UPDLOCK, HOLDLOCK)
        WHERE  [TenantId] = @tenant AND [DocumentType] = @type AND [FiscalYear] = @fiscalYear;

        IF @next IS NULL
        BEGIN
            SELECT @prefix = CASE @type
                                 WHEN 'Bill' THEN [BillPrefix]
                                 WHEN 'CreditNote' THEN [CreditNotePrefix]
                                 WHEN 'Payment' THEN [PaymentPrefix]
                                 WHEN 'Adjustment' THEN [AdjustmentPrefix]
                                 WHEN 'Refund' THEN [RefundPrefix]
                             END
            FROM   [dbo].[Tenants]
            WHERE  [Id] = @tenant;

            IF @prefix IS NOT NULL
            BEGIN
                INSERT [dbo].[NumberSeries] ([Id], [TenantId], [DocumentType], [FiscalYear], [Prefix], [NextNumber], [CreatedAtUtc], [UpdatedAtUtc])
                VALUES (NEWID(), @tenant, @type, @fiscalYear, @prefix, 1, @now, @now);

                SET @next = 1;
            END
        END

        IF @next IS NOT NULL
            UPDATE [dbo].[NumberSeries]
            SET    [NextNumber] = @next + 1, [UpdatedAtUtc] = @now
            WHERE  [TenantId] = @tenant AND [DocumentType] = @type AND [FiscalYear] = @fiscalYear;

        SELECT @next AS [SequenceNumber], @prefix AS [Prefix];
        """;

    /// <inheritdoc />
    public async Task HoldAsync(DocumentType type, FiscalYear fiscalYear, CancellationToken ct)
    {
        var tenantId = CheckArguments(type, fiscalYear, "A number series was held in a scope that has no tenant.");
        await EnsureTransactionAsync(ct);

        object[] parameters = [.. SeriesKey(tenantId, type, fiscalYear)];

        var askedAt = NumberSeriesTimer.Start();
#pragma warning disable RS0030 // Sanctioned raw SQL: the locked read of a number series, held for a prefix change (ADR 0033, built note point 5).
        _ = await context.Database.SqlQueryRaw<int>(HoldSeries, parameters).ToListAsync(ct);
#pragma warning restore RS0030
        timer?.Taken(askedAt);
    }

    /// <inheritdoc />
    public async Task<DocumentNumber> NextAsync(DocumentType type, FiscalYear fiscalYear, CancellationToken ct)
    {
        var tenantId = CheckArguments(type, fiscalYear, "A document number was asked for in a scope that has no tenant.");
        await EnsureTransactionAsync(ct);

        object[] parameters =
        [
            .. SeriesKey(tenantId, type, fiscalYear),
            new SqlParameter("@now", System.Data.SqlDbType.DateTimeOffset) { Value = clock.UtcNow },
        ];

        var askedAt = NumberSeriesTimer.Start();
#pragma warning disable RS0030 // Sanctioned raw SQL: the locked read-and-increment of a number series (ADR 0008, ADR 0015).
        var taken = await context.Database.SqlQueryRaw<TakenNumber>(TakeNext, parameters).ToListAsync(ct);
#pragma warning restore RS0030
        timer?.Taken(askedAt);

        if (taken is not [{ SequenceNumber: { } sequence, Prefix: { } prefix }])
        {
            throw new InvalidOperationException($"Tenant {tenantId} has no row, so it has no number series.");
        }

        try
        {
            return DocumentNumber.Create(type, fiscalYear, prefix, sequence);
        }
        catch (ArgumentException exception)
        {
            // The series has reached a number that no longer fits sixteen characters. The increment is rolled back
            // with the transaction; issuing a number a tax invoice may not carry is not an option (ADR 0008).
            throw new InvalidOperationException(
                $"The {type} series of fiscal year {fiscalYear} is exhausted: number {sequence} with prefix {prefix} does not fit {DocumentNumber.MaxLength} characters.",
                exception);
        }
    }

    // The parameters that name a series: the tenant of the current scope, never one passed in.
    private static SqlParameter[] SeriesKey(TenantId tenantId, DocumentType type, FiscalYear fiscalYear) =>
    [
        new SqlParameter("@tenant", tenantId.Value),
        new SqlParameter("@type", System.Data.SqlDbType.VarChar, 20) { Value = type.ToString() },
        new SqlParameter("@fiscalYear", System.Data.SqlDbType.Char, FiscalYear.LabelLength) { Value = fiscalYear.Label },
    ];

    private TenantId CheckArguments(DocumentType type, FiscalYear fiscalYear, string noTenant)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentException("The document type is not one the enum defines.", nameof(type));
        }

        if (fiscalYear.IsDefault)
        {
            throw new ArgumentException("A series belongs to a fiscal year; the default value is not one.", nameof(fiscalYear));
        }

        return tenantContext.TenantId ?? throw new InvalidOperationException(noTenant);
    }

    // The transaction of the use case: opened here if the use case has not saved or taken anything yet, and committed by
    // the unit of work. If the request ends without a commit, the context is disposed and the transaction rolls back,
    // and the number with it, and the lock ends.
    private async Task EnsureTransactionAsync(CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is null)
        {
            await context.Database.BeginTransactionAsync(ct);
        }
    }

    // What the statement returns: both null when the tenant has no row.
    private sealed record TakenNumber(int? SequenceNumber, string? Prefix);
}
