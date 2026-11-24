using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// The status of a request's hospital where no database is there to read it (ADR 0052): every tenant is active. The hosts
/// on a real database (<c>DatabaseApiFactory</c>) read the tenant's row instead.
/// </summary>
public sealed class ActiveTenants : ITenantStatusReader
{
    /// <inheritdoc />
    public Task<bool> IsActiveAsync(TenantId tenantId, CancellationToken ct) => Task.FromResult(true);
}
