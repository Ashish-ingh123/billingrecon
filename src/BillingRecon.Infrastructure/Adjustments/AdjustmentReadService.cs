using BillingRecon.Application.Adjustments;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Adjustments;

/// <summary>Reads adjustments through the request's context, untracked and filtered by its tenant.</summary>
public sealed class AdjustmentReadService(BillingReconDbContext context) : IAdjustmentReadService
{
    /// <inheritdoc />
    public async Task<AdjustmentDto?> GetAsync(AdjustmentId id, CancellationToken ct)
    {
        var adjustment = await Adjustments().SingleOrDefaultAsync(candidate => candidate.Id == id, ct);

        return adjustment?.ToDto();
    }

    /// <inheritdoc />
    public async Task<PageOf<AdjustmentDto>> ListAsync(ListAdjustmentsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var adjustments = Adjustments();
        if (query.BillId is { } billId)
        {
            adjustments = adjustments.Where(adjustment => adjustment.BillId == billId);
        }

        if (query.PayerId is { } payerId)
        {
            adjustments = adjustments.Where(adjustment => adjustment.PayerId == payerId);
        }

        if (query.Status is { } status)
        {
            adjustments = adjustments.Where(adjustment => adjustment.Status == status);
        }

        if (query.Kind is { } kind)
        {
            adjustments = adjustments.Where(adjustment => adjustment.Kind == kind);
        }

        var totalCount = await adjustments.CountAsync(ct);
        var page = await adjustments
            .OrderByDescending(adjustment => adjustment.AdjustmentDate)
            .ThenByDescending(adjustment => adjustment.CreatedAtUtc)
            .ThenBy(adjustment => adjustment.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PageOf<AdjustmentDto>([.. page.Select(adjustment => adjustment.ToDto())], query.Page, query.PageSize, totalCount);
    }

    private IQueryable<Adjustment> Adjustments() => context.Set<Adjustment>().AsNoTracking();
}
