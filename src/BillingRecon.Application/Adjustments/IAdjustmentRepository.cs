using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Adjustments;

/// <summary>
/// Loads and adds adjustments for the commands that change them. Reads are filtered by the current tenant. There is
/// no remove: a draft is voided, never deleted (BR-005).
/// </summary>
public interface IAdjustmentRepository
{
    /// <summary>The adjustment, tracked for a change, or null when the tenant has none with that id.</summary>
    public Task<Adjustment?> GetAsync(AdjustmentId id, CancellationToken ct);

    /// <summary>Adds a new adjustment to the unit of work; it is saved by the commit.</summary>
    public void Add(Adjustment adjustment);
}
