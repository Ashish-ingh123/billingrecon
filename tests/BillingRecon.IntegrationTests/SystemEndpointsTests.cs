using System.Net.Http.Json;
using BillingRecon.Api.Common;
using BillingRecon.IntegrationTests.Support;

namespace BillingRecon.IntegrationTests;

public sealed class SystemEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task System_info_returns_product_name_version_and_environment()
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());

        var info = await client.GetFromJsonAsync<SystemInfoResponse>(new Uri("/api/v1/system/info", UriKind.Relative));

        var body = info.ShouldNotBeNull();
        body.Application.ShouldBe("BillingRecon");
        body.Version.ShouldNotBeNullOrWhiteSpace();
        body.Environment.ShouldBe("Production");
    }
}
