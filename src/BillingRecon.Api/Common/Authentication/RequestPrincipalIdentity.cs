using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Api.Common.Authentication;

/// <summary>
/// The tenant, the user and the roles of the request (BR-002, BR-602; ADR 0031): those of its principal, which comes from the
/// token, or in Development from the development identity when the request carries none, or, in a sign-in, from the
/// user whose password has just been verified (<see cref="HttpSignInScope"/>). An anonymous request has neither, so
/// a tenant-scoped query or write in it fails (ADR 0009). Asked outside a request (a hosted service, a scope a test
/// opens) it throws, so nothing acts for a tenant by accident. Read again on every call, never kept: a sign-in changes
/// the principal in the middle of its request.
/// </summary>
public sealed class RequestPrincipalIdentity(IHttpContextAccessor accessor) : ITenantContext, ICurrentUser
{
    private const string OutsideRequestMessage =
        "The tenant or the user was asked for outside a request. Only a request has one (from its token); system work uses SystemIdentity, a hosted service a context of its own.";

    private const string IncompletePrincipalMessage =
        "The principal of the request is authenticated but has no tenant or no user that is an id; the authentication refuses such a token (ADR 0031).";

    /// <inheritdoc />
    public TenantId? TenantId => Read()?.TenantId;

    /// <inheritdoc />
    public UserId? UserId => Read()?.UserId;

    /// <inheritdoc />
    public IReadOnlyCollection<Role> Roles
    {
        get
        {
            var principal = accessor.HttpContext?.User ?? throw new InvalidOperationException(OutsideRequestMessage);

            return principal.Identity?.IsAuthenticated == true ? TokenClaims.RolesOf(principal) : [];
        }
    }

    private (TenantId TenantId, UserId UserId)? Read()
    {
        var principal = accessor.HttpContext?.User ?? throw new InvalidOperationException(OutsideRequestMessage);
        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return TokenClaims.TryRead(principal, out var tenantId, out var userId)
            ? (tenantId, userId)
            : throw new InvalidOperationException(IncompletePrincipalMessage);
    }
}
