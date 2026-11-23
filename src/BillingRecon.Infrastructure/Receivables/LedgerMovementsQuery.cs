using System.Data;
using BillingRecon.Application.Common;
using BillingRecon.Application.Receivables;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Receivables;

/// <summary>
/// The ledger of the current tenant summed by business date, for the dashboard (BR-902, ADR 0037): the balances of the
/// receivable accounts before a date, and the movements of each date of a range.
/// <para>
/// This is one of the files of the production code that send SQL of their own (ADR 0021, ADR 0037, BannedSymbolsTests),
/// for the reason <see cref="ReconciliationQuery"/> gives: sums of the ledger, grouped, which Entity Framework cannot
/// translate for amounts stored through a converter. It reads and never writes; the tenant is a parameter taken from the
/// current scope and is in every WHERE clause, the dates are parameters, and the two texts are constants.
/// </para>
/// </summary>
public sealed class LedgerMovementsQuery(BillingReconDbContext context, ITenantContext tenantContext) : ILedgerMovements
{
    private const string BalancesBefore =
        """
        SELECT ISNULL(SUM(CASE WHEN e.[Account] = 'Receivable'      THEN e.[Debit] - e.[Credit] ELSE 0 END), 0) AS [Receivable],
               ISNULL(SUM(CASE WHEN e.[Account] = 'UnappliedCash'   THEN e.[Credit] - e.[Debit] ELSE 0 END), 0) AS [UnappliedCash],
               ISNULL(SUM(CASE WHEN e.[Account] = 'CreditAvailable' THEN e.[Credit] - e.[Debit] ELSE 0 END), 0) AS [CreditAvailable]
        FROM   [dbo].[LedgerEntries] AS e
        JOIN   [dbo].[LedgerPostings] AS p ON p.[Id] = e.[PostingId] AND p.[TenantId] = @tenant
        WHERE  e.[TenantId] = @tenant AND p.[BusinessDate] < @date
        """;

    private const string Days =
        """
        SELECT p.[BusinessDate],
               SUM(CASE WHEN p.[Kind] = 'BillPosted'      AND e.[Account] = 'Receivable' THEN e.[Debit] ELSE 0 END) AS [Billed],
               SUM(CASE WHEN p.[Kind] = 'PaymentRecorded' AND e.[Account] = 'Cash'       THEN e.[Debit] ELSE 0 END) AS [Collected],
               SUM(CASE WHEN e.[Account] = 'Receivable'    THEN e.[Debit] - e.[Credit] ELSE 0 END) AS [ReceivableChange],
               SUM(CASE WHEN e.[Account] = 'UnappliedCash' THEN e.[Credit] - e.[Debit] ELSE 0 END) AS [UnappliedCashChange]
        FROM   [dbo].[LedgerEntries] AS e
        JOIN   [dbo].[LedgerPostings] AS p ON p.[Id] = e.[PostingId] AND p.[TenantId] = @tenant
        WHERE  e.[TenantId] = @tenant AND p.[BusinessDate] >= @from AND p.[BusinessDate] <= @to
        GROUP  BY p.[BusinessDate]
        ORDER  BY p.[BusinessDate]
        """;

    /// <inheritdoc />
    public async Task<LedgerBalances> BalancesBeforeAsync(DateOnly date, CancellationToken ct)
    {
#pragma warning disable RS0030 // Sanctioned raw SQL: read-only sums of the ledger before a date (ADR 0021, ADR 0037).
        var balances = await context.Database.SqlQueryRaw<Balances>(BalancesBefore, Tenant(), Date("@date", date)).ToListAsync(ct);
#pragma warning restore RS0030

        var row = balances.Single();

        return new LedgerBalances(row.Receivable, row.UnappliedCash, row.CreditAvailable);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LedgerDay>> DaysAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
#pragma warning disable RS0030 // Sanctioned raw SQL: read-only sums of the ledger by business date (ADR 0021, ADR 0037).
        var days = await context.Database.SqlQueryRaw<Day>(Days, Tenant(), Date("@from", from), Date("@to", to)).ToListAsync(ct);
#pragma warning restore RS0030

        return [.. days.Select(day => new LedgerDay(day.BusinessDate, day.Billed, day.Collected, day.ReceivableChange, day.UnappliedCashChange))];
    }

    private SqlParameter Tenant()
    {
        var tenantId = tenantContext.TenantId
            ?? throw new InvalidOperationException("The ledger was asked for its movements in a scope that has no tenant.");

        return new SqlParameter("@tenant", tenantId.Value);
    }

    private static SqlParameter Date(string name, DateOnly date) => new(name, SqlDbType.Date) { Value = date };

    // What the statements return.
    private sealed record Balances(decimal Receivable, decimal UnappliedCash, decimal CreditAvailable);

    private sealed record Day(DateOnly BusinessDate, decimal Billed, decimal Collected, decimal ReceivableChange, decimal UnappliedCashChange);
}
