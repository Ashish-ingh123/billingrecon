using System.Security.Claims;
using BillingRecon.Api.Common.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// Stands in for the authentication middleware the Identity slice will add: a request that carries the test headers
/// runs as an authenticated user with those tenant and user ids and roles. Requests without them stay anonymous.
/// <see cref="TestClientExtensions"/> sets the headers on every request of a client.
/// </summary>
internal sealed class TestPrincipalStartupFilter : IStartupFilter
{
    public const string TenantHeader = "X-Test-Tenant";
    public const string UserHeader = "X-Test-User";

    /// <summary>Role names, separated by commas.</summary>
    public const string RolesHeader = "X-Test-Roles";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue(TenantHeader, out var tenantId))
            {
                var claims = new List<Claim>
                {
                    new(RequestIdentity.TenantIdClaim, tenantId.ToString()),
                    new(RequestIdentity.UserIdClaim, context.Request.Headers[UserHeader].ToString()),
                };

                var roles = context.Request.Headers[RolesHeader].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

                context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test", nameType: ClaimTypes.Name, roleType: ClaimTypes.Role));
            }

            await nextMiddleware(context);
        });

        next(app);
    };
}
