using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>
/// What a sign-in reads before anyone is signed in (ADR 0031, point 6): the tenant by its code, and then the user by
/// its address within that tenant. The second read is the one sanctioned use of <c>IgnoreQueryFilters</c>: a sign-in has
/// no tenant in scope yet.
/// </summary>
public interface ILoginUserLookup
{
    /// <summary>The tenant with the code <paramref name="code"/>; null when there is none.</summary>
    public Task<LoginTenant?> FindTenantAsync(string code, CancellationToken ct);

    /// <summary>The user of <paramref name="tenantId"/> with the address <paramref name="normalizedEmail"/>; null when there is none.</summary>
    public Task<LoginUser?> FindUserAsync(TenantId tenantId, string normalizedEmail, CancellationToken ct);
}
