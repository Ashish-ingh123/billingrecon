using System.Net;
using System.Net.Http.Json;
using BillingRecon.Api.Common.Authentication;
using BillingRecon.Api.Identity;
using BillingRecon.Application.Identity;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The bootstrap of the demo (ADR 0045 point 1): with the demo on, the API starts with the demo hospital and its
/// administrator, who signs in with the configured password; a second start changes nothing.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class DemoTenantSeederTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private const string Password = "demo password for the test";

    private static readonly Uri Login = new("/api/v1/auth/login", UriKind.Relative);

    [Fact]
    public async Task With_the_demo_on_its_administrator_signs_in_to_the_demo_hospital_and_a_second_start_changes_nothing()
    {
        for (var start = 0; start < 2; start += 1)
        {
            using var factory = new DatabaseApiFactory(SqlServer, configure: services =>
                services.PostConfigure<DemoOptions>(options =>
                {
                    options.Enabled = true;
                    options.AdminPassword = Password;
                }),
                environment: DemoOptions.EnvironmentName);
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync(Login, new LoginRequest { Tenant = DemoOptions.TenantCode, Email = DemoOptions.AdminEmail, Password = Password });

            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var token = new JsonWebToken((await response.ReadAsAsync<SignedInDto>()).AccessToken);
            token.Subject.ShouldBe(DemoOptions.AdminUserId.ToString());
            token.GetClaim(TokenClaims.RoleClaim).Value.ShouldBe("TenantAdmin");
        }
    }

    [Fact]
    public async Task With_the_demo_off_there_is_no_demo_hospital()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Login, new LoginRequest { Tenant = DemoOptions.TenantCode, Email = DemoOptions.AdminEmail, Password = Password });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
