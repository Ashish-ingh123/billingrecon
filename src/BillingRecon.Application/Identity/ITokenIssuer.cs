using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>
/// Issues the token a signed-in user sends with every request (BR-602; ADR 0031): the tenant, the user and the roles,
/// signed, valid for 60 minutes from the application's clock.
/// </summary>
public interface ITokenIssuer
{
    /// <summary>A new token for user <paramref name="userId"/> of tenant <paramref name="tenantId"/>, holding <paramref name="roles"/>.</summary>
    public AccessToken Issue(TenantId tenantId, UserId userId, IReadOnlyCollection<Role> roles);
}
