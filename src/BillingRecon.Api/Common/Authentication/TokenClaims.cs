using System.Security.Claims;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Api.Common.Authentication;

/// <summary>
/// The claims of the API's tokens, and of every principal a request acts as (ADR 0031, point 3): <c>sub</c>, the id of
/// the user; <c>tenant_id</c>, the id of the tenant; one <c>role</c> per role, by name. The token, the sign-in and the
/// development identity build their principal here, so the three cannot differ, and the identity of a request reads it
/// back here.
/// </summary>
public static class TokenClaims
{
    /// <summary>The claim that carries the id of the tenant (BR-602).</summary>
    public const string TenantIdClaim = "tenant_id";

    /// <summary>The claim that carries the id of the user (BR-602).</summary>
    public const string UserIdClaim = "sub";

    /// <summary>The claim that carries a role, one claim per role (BR-602).</summary>
    public const string RoleClaim = "role";

    /// <summary>The claims for <paramref name="userId"/> of <paramref name="tenantId"/> holding <paramref name="roles"/>.</summary>
    public static IEnumerable<Claim> For(TenantId tenantId, UserId userId, IEnumerable<Role> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        yield return new Claim(UserIdClaim, userId.Value.ToString());
        yield return new Claim(TenantIdClaim, tenantId.Value.ToString());

        foreach (var role in roles)
        {
            yield return new Claim(RoleClaim, role.ToString());
        }
    }

    /// <summary>An authenticated principal with the claims of <see cref="For"/>, authenticated by <paramref name="authenticationType"/>.</summary>
    public static ClaimsPrincipal Principal(TenantId tenantId, UserId userId, IEnumerable<Role> roles, string authenticationType)
        => new(new ClaimsIdentity(For(tenantId, userId, roles), authenticationType, UserIdClaim, RoleClaim));

    /// <summary>
    /// The tenant and the user of <paramref name="principal"/>: true when it carries exactly one of each claim and both
    /// are ids. The authentication refuses a token for which this is false.
    /// </summary>
    public static bool TryRead(ClaimsPrincipal principal, out TenantId tenantId, out UserId userId)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var hasTenant = TryReadId(principal, TenantIdClaim, out var tenant);
        var hasUser = TryReadId(principal, UserIdClaim, out var user);
        tenantId = new TenantId(tenant);
        userId = new UserId(user);

        return hasTenant && hasUser;
    }

    /// <summary>
    /// The roles <paramref name="principal"/> holds, in the order of <see cref="Role"/>: the <c>role</c> claims that name
    /// a role exactly. Anything else in such a claim is no role at all.
    /// </summary>
    public static IReadOnlyList<Role> RolesOf(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return [.. Enum.GetValues<Role>().Where(role => principal.HasClaim(RoleClaim, role.ToString()))];
    }

    private static bool TryReadId(ClaimsPrincipal principal, string type, out Guid id)
    {
        id = Guid.Empty;
        var claims = principal.FindAll(type).Take(2).ToArray();

        return claims.Length == 1 && Guid.TryParseExact(claims[0].Value, "D", out id) && id != Guid.Empty;
    }
}
