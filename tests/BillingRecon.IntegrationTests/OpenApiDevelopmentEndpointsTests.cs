using System.Net;

namespace BillingRecon.IntegrationTests;

/// <summary>The built-in OpenAPI document and the Scalar UI that renders it are served in Development.</summary>
public sealed class OpenApiDevelopmentEndpointsTests(DevelopmentApiFactory factory) : IClassFixture<DevelopmentApiFactory>
{
    [Fact]
    public async Task OpenApi_document_and_Scalar_ui_are_served_in_development()
    {
        using var client = factory.CreateClient();

        using var document = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        using var ui = await client.GetAsync(new Uri("/scalar", UriKind.Relative));

        document.StatusCode.ShouldBe(HttpStatusCode.OK);
        ui.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
