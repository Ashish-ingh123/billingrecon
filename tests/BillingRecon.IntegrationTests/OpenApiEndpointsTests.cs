using System.Net;

namespace BillingRecon.IntegrationTests;

/// <summary>The OpenAPI document and the Scalar UI exist in Development only.</summary>
public sealed class OpenApiEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/scalar")]
    [InlineData("/scalar/v1")]
    public async Task OpenApi_document_and_Scalar_ui_are_not_served_outside_development(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
