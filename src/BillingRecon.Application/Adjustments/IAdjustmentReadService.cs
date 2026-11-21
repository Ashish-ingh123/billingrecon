using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Adjustments;

/// <summary>Reads adjustments without tracking them, for queries. Every read is filtered by the current tenant.</summary>
public interface IAdjustmentReadService
{
    /// <summary>The adjustment, or null when the tenant has none with that id.</summary>
    public Task<AdjustmentDto?> GetAsync(AdjustmentId id, CancellationToken ct);

    /// <summary>One page of the tenant's adjustments that match <paramref name="query"/>: the newest date first, then the newest adjustment.</summary>
    public Task<PageOf<AdjustmentDto>> ListAsync(ListAdjustmentsQuery query, CancellationToken ct);
}
