using Microsoft.AspNetCore.Authorization;

namespace BillingRecon.Api.Common.Authorization;

/// <summary>
/// What a signed-in user may do (ADR 0032): the policies of <see cref="Policies"/>, each admitting its roles, and the
/// fallback that needs a signed-in user wherever no policy is named (deny by default, ADR 0031 point 7).
/// </summary>
public static class PermissionsServiceCollectionExtensions
{
    /// <summary>Adds the fallback policy and every named policy.</summary>
    public static IServiceCollection AddPermissions(this IServiceCollection services)
    {
        var authorization = services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.SignedIn, policy => policy.RequireAuthenticatedUser());

        foreach (var (name, roles) in Policies.Roles)
        {
            authorization.AddPolicy(name, policy => policy.RequireAuthenticatedUser().RequireRole(roles.Select(role => role.ToString())));
        }

        return services;
    }
}
