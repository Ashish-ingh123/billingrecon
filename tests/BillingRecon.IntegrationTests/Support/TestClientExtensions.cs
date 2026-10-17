using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>Clients whose every request acts for a given tenant, user and roles, through the test headers.</summary>
internal static class TestClientExtensions
{
    /// <summary>A client that acts for <paramref name="tenantId"/> as <paramref name="userId"/>, holding <paramref name="roles"/>.</summary>
    public static HttpClient CreateClientFor(this WebApplicationFactory<Program> factory, TenantId tenantId, UserId userId, params string[] roles)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestPrincipalStartupFilter.TenantHeader, tenantId.ToString());
        client.DefaultRequestHeaders.Add(TestPrincipalStartupFilter.UserHeader, userId.ToString());

        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add(TestPrincipalStartupFilter.RolesHeader, string.Join(',', roles));
        }

        return client;
    }

    /// <summary>A client that acts for the tenant and user of <paramref name="identity"/>, holding <paramref name="roles"/>.</summary>
    public static HttpClient CreateClientFor(this WebApplicationFactory<Program> factory, FixedIdentity identity, params string[] roles)
        => factory.CreateClientFor(
            identity.TenantId ?? throw new ArgumentException("The identity has no tenant.", nameof(identity)),
            identity.UserId ?? throw new ArgumentException("The identity has no user.", nameof(identity)),
            roles);
}
