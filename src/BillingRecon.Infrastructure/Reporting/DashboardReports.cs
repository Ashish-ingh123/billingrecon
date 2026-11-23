using BillingRecon.Application.Reporting;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Reporting;

/// <summary>
/// What the dashboard reads besides the ledger (BR-902, ADR 0037), without tracking and filtered by the tenant of the
/// context: the posted bills that still owe something, and the requests that may have been waiting in a window. The
/// arithmetic is the Application's; this only reads.
/// </summary>
public sealed class DashboardReports(BillingReconDbContext context) : IDashboardReports
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<OpenBillDto>> OpenBillsAsync(CancellationToken ct)
    {
        var bills = await context.Set<Bill>().AsNoTracking()
            .Where(bill => bill.Number != null && bill.BalanceDue != Money.Zero)
            .Where(bill => bill.Status == BillStatus.Posted || bill.Status == BillStatus.PartiallyPaid || bill.Status == BillStatus.Settled)
            .Select(bill => new { bill.DueDate, bill.BalanceDue })
            .ToListAsync(ct);

        return [.. bills.Select(bill => new OpenBillDto(bill.DueDate, bill.BalanceDue.Amount))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApprovalSpanDto>> ApprovalSpansAsync(DateTimeOffset notDecidedBefore, DateTimeOffset before, CancellationToken ct)
    {
        var requests = await context.Set<ApprovalRequest>().AsNoTracking()
            .Where(request => request.RequestedAtUtc < before && (request.DecidedAtUtc == null || request.DecidedAtUtc >= notDecidedBefore))
            .Select(request => new { request.RequestedAtUtc, request.DecidedAtUtc })
            .ToListAsync(ct);

        return [.. requests.Select(request => new ApprovalSpanDto(request.RequestedAtUtc, request.DecidedAtUtc))];
    }
}
