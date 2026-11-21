using BillingRecon.Application.Adjustments;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Adjustments;

/// <summary>Loads adjustments for a change and adds new ones, through the request's context: filtered by its tenant, saved by its unit of work.</summary>
public sealed class AdjustmentRepository(BillingReconDbContext context) : IAdjustmentRepository
{
    /// <inheritdoc />
    public Task<Adjustment?> GetAsync(AdjustmentId id, CancellationToken ct)
        => context.Set<Adjustment>().SingleOrDefaultAsync(adjustment => adjustment.Id == id, ct);

    /// <inheritdoc />
    public void Add(Adjustment adjustment) => context.Set<Adjustment>().Add(adjustment);
}
