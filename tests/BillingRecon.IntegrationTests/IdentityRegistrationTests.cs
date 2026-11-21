using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillingRecon.Api.Common.Authentication;
using BillingRecon.Application.Common;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// Who a request acts for (ADR 0031): in every environment the principal of the request, which only a valid token gives
/// it; in Development also the development user, for a request without a token; outside a request nobody, and asking
/// fails.
/// </summary>
public sealed class IdentityRegistrationTests
{
    private static readonly Uri Identity = new("/api/v1/probe/identity", UriKind.Relative);

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    public void In_every_environment_the_identity_is_the_principal_of_the_request(string environment)
    {
        using var factory = new EnvironmentApiFactory(environment);
        using var scope = factory.Services.CreateScope();

        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var user = scope.ServiceProvider.GetRequiredService<ICurrentUser>();

        tenant.ShouldBeOfType<RequestPrincipalIdentity>();
        user.ShouldBeSameAs(tenant);
    }

    [Fact]
    public void Outside_a_request_there_is_no_identity_and_asking_for_one_fails()
    {
        using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();

        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var user = scope.ServiceProvider.GetRequiredService<ICurrentUser>();

        Should.Throw<InvalidOperationException>(() => tenant.TenantId).Message.ShouldContain("outside a request");
        Should.Throw<InvalidOperationException>(() => user.UserId).Message.ShouldContain("outside a request");
    }

    [Fact]
    public async Task In_development_a_request_without_a_token_acts_for_the_configured_tenant_and_user()
    {
        using var factory = new DevelopmentApiFactory();
        using var client = factory.CreateClient();

        var body = (await client.GetFromJsonAsync<IdentityResponse>(Identity)).ShouldNotBeNull();

        // The values of appsettings.Development.json; the development user administers its tenant.
        body.TenantId.ShouldBe(Guid.Parse("d0000000-0000-4000-8000-000000000001"));
        body.UserId.ShouldBe(Guid.Parse("d0000000-0000-4000-8000-000000000002"));
        body.Roles.ShouldBe(["TenantAdmin"]);
    }

    [Fact]
    public async Task In_development_a_request_with_a_token_acts_for_the_token()
    {
        using var factory = new DevelopmentApiFactory();
        var identity = FixedIdentity.Random();
        using var client = factory.CreateClientFor(identity, "Cashier");

        var body = (await client.GetFromJsonAsync<IdentityResponse>(Identity)).ShouldNotBeNull();

        body.TenantId.ShouldBe(identity.TenantId!.Value.Value);
        body.UserId.ShouldBe(identity.UserId!.Value.Value);
        body.Roles.ShouldBe(["Cashier"]);
    }

    [Fact]
    public async Task In_development_a_request_with_a_bad_token_is_refused_and_does_not_fall_back()
    {
        using var factory = new DevelopmentApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Identity);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");

        using var response = await client.SendAsync(request);

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task The_development_identity_is_not_even_registered_outside_development()
    {
        using var factory = new ApiFactory();

        var schemes = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        (await schemes.GetSchemeAsync(DevelopmentAuthenticationHandler.SchemeName)).ShouldBeNull();
        factory.Services.GetServices<IHostedService>().ShouldNotContain(service => service is DevelopmentTenantSeeder);
    }

    [Theory]
    [InlineData("DevelopmentIdentity:TenantId")]
    [InlineData("DevelopmentIdentity:UserId")]
    public void In_development_the_api_refuses_to_start_without_both_ids(string missing)
    {
        using var factory = new IncompleteIdentityApiFactory(missing);

        var exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        exception.Message.ShouldContain("DevelopmentIdentity:TenantId and DevelopmentIdentity:UserId are both required");
    }

    private sealed class EnvironmentApiFactory(string environment) : ApiFactory
    {
        protected override string EnvironmentName => environment;
    }

    private sealed class IncompleteIdentityApiFactory(string missing) : ApiFactory
    {
        protected override string EnvironmentName => Environments.Development;

        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [
            .. base.Settings,
            new(missing, Guid.Empty.ToString()),
        ];
    }
}
