using BillingRecon.Application.Adjustments;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Adjustments;

/// <summary>
/// The posted adjustments of a bill, summed (ADR 0025, amended 2026-11-26), through the context and its tenant filter.
/// Read without tracking, so that a second reading under the ledger's lock sees what another request committed.
/// </summary>
public sealed class BillAdjustments(BillingReconDbContext context) : IBillAdjustments
{
    /// <inheritdoc />
    public async Task<Money> SumPostedAsync(BillId billId, CancellationToken ct)
    {
        var amounts = await context.Set<Adjustment>()
            .AsNoTracking()
            .Where(adjustment => adjustment.BillId == billId && adjustment.Status == AdjustmentStatus.Posted)
            .Select(adjustment => adjustment.Amount)
            .ToListAsync(ct);

        return amounts.Sum();
    }
}
