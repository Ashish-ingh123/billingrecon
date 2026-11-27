using System.Globalization;
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

    /// <summary>
    /// Present, with the value <see cref="PasswordChangeRequired"/>, on a token whose user must set a password of their
    /// own first (ADR 0051): such a token reaches the change of one's own password and nothing else.
    /// </summary>
    public const string PasswordChangeClaim = "pwd_change";

    /// <summary>The value of <see cref="PasswordChangeClaim"/>.</summary>
    public const string PasswordChangeRequired = "required";

    /// <summary>The claim of a session's number (ADR 0057).</summary>
    public const string SessionClaim = "ses";

    /// <summary>
    /// The claim of the second step (ADR 0061): <c>pending</c> on a token that only gives its code, <c>enrol</c> on one
    /// that only enrols it, <c>done</c> on a full token whose sign-in gave it. A token without it is a full token of a
    /// user who has no second step.
    /// </summary>
    public const string SecondStepClaim = "step";

    /// <summary>The stage of the second step a token carries; <see cref="SecondStepStage.None"/> for a token without the claim.</summary>
    public static SecondStepStage SecondStepOf(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal.FindFirst(SecondStepClaim)?.Value switch
        {
            "pending" => SecondStepStage.Pending,
            "enrol" => SecondStepStage.Enrolment,
            "done" => SecondStepStage.Done,
            _ => SecondStepStage.None,
        };
    }

    /// <summary>The value of the claim for a stage; null for <see cref="SecondStepStage.None"/>, which has no claim.</summary>
    public static string? SecondStepValue(SecondStepStage stage) => stage switch
    {
        SecondStepStage.Pending => "pending",
        SecondStepStage.Enrolment => "enrol",
        SecondStepStage.Done => "done",
        _ => null,
    };

    /// <summary>
    /// The number of the session <paramref name="principal"/> was issued in: true when it carries exactly one such claim
    /// and it is a whole number that is not negative.
    /// </summary>
    public static bool TryReadSession(ClaimsPrincipal principal, out int sessionVersion)
    {
        ArgumentNullException.ThrowIfNull(principal);

        sessionVersion = 0;
        var claims = principal.FindAll(SessionClaim).Take(2).ToArray();

        return claims.Length == 1
            && int.TryParse(claims[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out sessionVersion);
    }

    /// <summary>Whether the principal carries the mark of a due password change.</summary>
    public static bool MustChangePassword(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.HasClaim(PasswordChangeClaim, PasswordChangeRequired);
    }

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
