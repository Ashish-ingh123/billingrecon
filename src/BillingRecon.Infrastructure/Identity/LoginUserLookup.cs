using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// What a sign-in reads before anyone is signed in (ADR 0031, point 6; ADR 0015). The tenant is found by its code
/// without switching anything off: a scope without a tenant sees every tenant row. The user is found by its address
/// in that tenant with the query filters switched off, because a sign-in has no tenant in scope and the filter would
/// match nothing; the tenant is therefore in the query by hand. This is the one sanctioned use of
/// <c>IgnoreQueryFilters</c>, and <c>BannedSymbolsTests</c> keeps it so.
/// </summary>
public sealed class LoginUserLookup(BillingReconDbContext context) : ILoginUserLookup
{
    /// <inheritdoc />
    public async Task<LoginTenant?> FindTenantAsync(string code, CancellationToken ct)
    {
        var tenant = await context.Set<Tenant>().AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Code == code, ct);

        return tenant is null ? null : new LoginTenant(tenant.Id, tenant.Status);
    }

    /// <inheritdoc />
    public async Task<LoginUser?> FindUserAsync(TenantId tenantId, string normalizedEmail, CancellationToken ct)
    {
#pragma warning disable RS0030 // The one sanctioned use of IgnoreQueryFilters: a sign-in has no tenant yet (ADR 0015, ADR 0031).
        var user = await context.Set<User>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(candidate => candidate.Roles)
            .Where(candidate => candidate.TenantId == tenantId && candidate.NormalizedEmail == normalizedEmail)
            .SingleOrDefaultAsync(ct);
#pragma warning restore RS0030

        return user is null ? null : new LoginUser(user.Id, user.Status, user.PasswordHash, [.. user.Roles.Select(role => role.Role).Order()]);
    }
}
