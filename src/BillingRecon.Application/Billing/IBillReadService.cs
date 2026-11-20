using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Reads bills without tracking them, for queries. Every read is filtered by the current tenant, so another tenant's
/// bill is not there at all.
/// </summary>
public interface IBillReadService
{
    /// <summary>The bill with its lines, or null when the tenant has no bill with that id.</summary>
    public Task<BillDto?> GetAsync(BillId id, CancellationToken ct);

    /// <summary>One page of the tenant's bills that match <paramref name="query"/>: the newest bill date first, then the newest bill.</summary>
    public Task<PageOf<BillSummaryDto>> ListAsync(ListBillsQuery query, CancellationToken ct);
}
