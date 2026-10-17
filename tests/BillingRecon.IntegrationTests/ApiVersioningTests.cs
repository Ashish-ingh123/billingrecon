using System.Net;
using BillingRecon.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.IntegrationTests;

/// <summary>The API is versioned under /api/v1 (docs/requirements.md section 5, docs/adr/0013-api-versioning-by-route-prefix.md).</summary>
public sealed class ApiVersioningTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public void Every_controller_route_is_under_the_v1_prefix()
    {
        var controllerRoutes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToArray();

        controllerRoutes.ShouldContain("api/v1/system/info");
        controllerRoutes.ShouldAllBe(route => route.StartsWith("api/v1/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_unversioned_path_does_not_exist()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/system/info", UriKind.Relative));

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "not_found");
    }
}
