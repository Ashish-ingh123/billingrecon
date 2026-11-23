using System.Net;
using BillingRecon.Api.Common.Transport;
using BillingRecon.IntegrationTests.Support;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// The headers that keep a browser or a cache from misusing an answer (ADR 0035): every response says it is not to be
/// sniffed; every answer of <c>/api</c>, an error included, is not to be stored, loads nothing and is shown in no frame.
/// </summary>
public sealed class SecurityHeadersTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("/api/v1/system/info", HttpStatusCode.OK)]
    [InlineData("/api/v1/does-not-exist", HttpStatusCode.NotFound)]
    [InlineData("/api/v1/probe/throw", HttpStatusCode.InternalServerError)]
    public async Task Every_answer_of_the_API_is_not_sniffed_not_stored_and_not_framed(string path, HttpStatusCode status)
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.ShouldBe(status);
        response.Headers.GetValues("X-Content-Type-Options").ShouldHaveSingleItem().ShouldBe("nosniff");
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        response.Headers.GetValues("Content-Security-Policy").ShouldHaveSingleItem().ShouldBe(SecurityHeadersMiddleware.ContentSecurityPolicy);
    }

    [Fact]
    public async Task A_refused_request_carries_them_too()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/system/info", UriKind.Relative));

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "unauthorized");
        response.Headers.GetValues("X-Content-Type-Options").ShouldHaveSingleItem().ShouldBe("nosniff");
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        response.Headers.GetValues("Content-Security-Policy").ShouldHaveSingleItem().ShouldBe(SecurityHeadersMiddleware.ContentSecurityPolicy);
    }

    [Fact]
    public async Task A_health_answer_is_not_sniffed_and_keeps_the_rest_to_itself()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("X-Content-Type-Options").ShouldHaveSingleItem().ShouldBe("nosniff");
        response.Headers.Contains("Content-Security-Policy").ShouldBeFalse();
    }
}
