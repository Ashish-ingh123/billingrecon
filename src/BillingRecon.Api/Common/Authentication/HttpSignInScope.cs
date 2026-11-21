using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Api.Common.Authentication;

/// <summary>
/// The step of a sign-in from nobody to the user whose password it has verified (ADR 0031, point 5): the principal of
/// the request becomes the one the token will carry, so the rest of the request (the write of the user's row, its
/// stamps, its audit record) acts as that user. Only the sign-in use case calls it, an architecture test keeps it so,
/// and only in a request that acts for nobody: the sign-in endpoint is anonymous, and an anonymous endpoint
/// authenticates nobody, whatever the request carries (<see cref="AnonymousEndpoints"/>).
/// </summary>
public sealed class HttpSignInScope(IHttpContextAccessor accessor) : ISignInScope
{
    /// <summary>The authentication type of the principal a sign-in enters.</summary>
    public const string AuthenticationType = "SignIn";

    /// <inheritdoc />
    public void Enter(TenantId tenantId, UserId userId, IReadOnlyCollection<Role> roles)
    {
        var context = accessor.HttpContext ?? throw new InvalidOperationException("A sign-in runs in a request.");
        if (context.User.Identity?.IsAuthenticated == true)
        {
            throw new InvalidOperationException(
                "A request that already acts for a user cannot sign in: the sign-in endpoint must stay anonymous, and an anonymous endpoint authenticates nobody (ADR 0031).");
        }

        context.User = TokenClaims.Principal(tenantId, userId, roles, AuthenticationType);
    }
}
