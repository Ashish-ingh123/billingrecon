using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>
/// Whether the hospital of a request may act (ADR 0052): a suspended one is refused on every request, also with a token
/// issued before its suspension. The answer may be up to 30 seconds old.
/// </summary>
public interface ITenantStatusReader
{
    /// <summary>True when the tenant is active.</summary>
    public Task<bool> IsActiveAsync(TenantId tenantId, CancellationToken ct);
}
