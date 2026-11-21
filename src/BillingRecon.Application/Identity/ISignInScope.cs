using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>
/// Makes the sign-in request act as the user whose password it has just verified (ADR 0031, point 5): from then on the
/// request's tenant and user are that user's, as the token about to be issued will say. The one place where the tenant
/// of a request comes from verified credentials instead of a token; an architecture test keeps it to the sign-in use
/// case.
/// </summary>
public interface ISignInScope
{
    /// <summary>The request acts as <paramref name="userId"/> of <paramref name="tenantId"/>, with <paramref name="roles"/>, from now on.</summary>
    public void Enter(TenantId tenantId, UserId userId, IReadOnlyCollection<Role> roles);
}
