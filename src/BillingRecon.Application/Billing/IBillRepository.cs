using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Loads and adds bills for the commands that change them (docs/domain-model.md, section 9). A bill is loaded whole,
/// with its lines. Reads are filtered by the current tenant. There is no remove: a draft is voided, never deleted
/// (BR-005, BR-204).
/// </summary>
public interface IBillRepository
{
    /// <summary>The bill with its lines, tracked for a change, or null when the tenant has no bill with that id.</summary>
    public Task<Bill?> GetAsync(BillId id, CancellationToken ct);

    /// <summary>Adds a new bill to the unit of work; it is saved by the commit.</summary>
    public void Add(Bill bill);
}
