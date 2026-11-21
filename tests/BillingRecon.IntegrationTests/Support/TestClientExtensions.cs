using System.Net.Http.Headers;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// Clients whose every request acts for a given tenant, user and roles the way a signed-in client does: each request
/// carries a bearer token minted for it by the host's own issuer (ADR 0031, point 10). So every request goes through the
/// whole validation of tokens, and a test that moves the host's clock never leaves a client holding an expired token.
/// A request that sets its own <c>Authorization</c> header keeps it.
/// </summary>
internal static class TestClientExtensions
{
    /// <summary>A client that acts for <paramref name="tenantId"/> as <paramref name="userId"/>, holding <paramref name="roles"/>.</summary>
    public static HttpClient CreateClientFor(this WebApplicationFactory<Program> factory, TenantId tenantId, UserId userId, params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var issuer = factory.Services.GetRequiredService<ITokenIssuer>();

        return factory.CreateDefaultClient(new BearerTokens(issuer, tenantId, userId, TestRoles.Of(roles)));
    }

    /// <summary>A client that acts for the tenant and user of <paramref name="identity"/>, holding <paramref name="roles"/>.</summary>
    public static HttpClient CreateClientFor(this WebApplicationFactory<Program> factory, FixedIdentity identity, params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(identity);

        return factory.CreateClientFor(
            identity.TenantId ?? throw new ArgumentException("The identity has no tenant.", nameof(identity)),
            identity.UserId ?? throw new ArgumentException("The identity has no user.", nameof(identity)),
            roles);
    }

    /// <summary>A token of the host for <paramref name="userId"/> of <paramref name="tenantId"/>, for a test that sets the header itself.</summary>
    public static string TokenFor(this WebApplicationFactory<Program> factory, TenantId tenantId, UserId userId, params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(factory);

        return factory.Services.GetRequiredService<ITokenIssuer>().Issue(tenantId, userId, TestRoles.Of(roles)).Value;
    }

    private sealed class BearerTokens(ITokenIssuer issuer, TenantId tenantId, UserId userId, IReadOnlyList<Role> roles) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Authorization ??= new AuthenticationHeaderValue("Bearer", issuer.Issue(tenantId, userId, roles).Value);

            return base.SendAsync(request, cancellationToken);
        }
    }
}
