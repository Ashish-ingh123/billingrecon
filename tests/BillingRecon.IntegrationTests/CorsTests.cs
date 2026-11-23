using System.Net;
using BillingRecon.IntegrationTests.Support;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// The frontend's origins (ADR 0035): a browser on an origin of <c>Cors:Origins</c> may call the API, with the methods
/// and headers the API takes and reading the headers it answers with; no credentials, since the token travels in a header;
/// a browser on any other origin gets no permission; and with no origin configured, none at all.
/// </summary>
public sealed class CorsTests(TransportApiFactory factory) : IClassFixture<TransportApiFactory>
{
    private const string AllowOrigin = "Access-Control-Allow-Origin";

    private static readonly Uri Bills = new("/api/v1/bills", UriKind.Relative);
    private static readonly Uri SystemInfo = new("/api/v1/system/info", UriKind.Relative);

    [Fact]
    public async Task A_preflight_from_the_frontend_is_answered_without_a_token_with_what_the_API_takes()
    {
        using var client = factory.CreateClient();
        using var preflight = Preflight(TransportApiFactory.FrontendOrigin, "POST", "authorization, content-type, idempotency-key, if-match, x-correlation-id");

        using var response = await client.SendAsync(preflight);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.GetValues(AllowOrigin).ShouldHaveSingleItem().ShouldBe(TransportApiFactory.FrontendOrigin);
        Listed(response, "Access-Control-Allow-Methods").ShouldBe(["GET", "POST", "PUT", "PATCH"], ignoreOrder: true);
        Listed(response, "Access-Control-Allow-Headers").Select(name => name.ToLowerInvariant())
            .ShouldBe(["authorization", "content-type", "idempotency-key", "if-match", "x-correlation-id"], ignoreOrder: true);
        response.Headers.GetValues("Access-Control-Max-Age").ShouldHaveSingleItem().ShouldBe("600");
        response.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();
    }

    [Fact]
    public async Task A_preflight_for_a_method_the_API_never_takes_is_told_the_four_it_does_so_the_browser_stops()
    {
        using var client = factory.CreateClient();
        using var preflight = Preflight(TransportApiFactory.FrontendOrigin, "DELETE", requestHeaders: null);

        using var response = await client.SendAsync(preflight);

        Listed(response, "Access-Control-Allow-Methods").ShouldBe(["GET", "POST", "PUT", "PATCH"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://app.billingrecon.test")]
    [InlineData("https://app.billingrecon.test:8443")]
    [InlineData("null")]
    public async Task A_browser_on_any_other_origin_gets_no_permission(string origin)
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());
        using var preflight = Preflight(origin, "POST", "authorization");
        using var request = new HttpRequestMessage(HttpMethod.Get, SystemInfo);
        request.Headers.Add("Origin", origin);

        using var preflightResponse = await client.SendAsync(preflight);
        using var response = await client.SendAsync(request);

        preflightResponse.Headers.Contains(AllowOrigin).ShouldBeFalse();
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains(AllowOrigin).ShouldBeFalse();
    }

    [Fact]
    public async Task An_answer_to_the_frontend_lets_it_read_the_headers_a_client_of_this_API_needs()
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());
        using var request = new HttpRequestMessage(HttpMethod.Get, SystemInfo);
        request.Headers.Add("Origin", TransportApiFactory.FrontendOrigin);

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues(AllowOrigin).ShouldHaveSingleItem().ShouldBe(TransportApiFactory.FrontendOrigin);
        Listed(response, "Access-Control-Expose-Headers").ShouldBe(["ETag", "Location", "Retry-After", "X-Correlation-Id", "Idempotent-Replayed"], ignoreOrder: true);
        response.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();
    }

    [Fact]
    public async Task A_refusal_reaches_the_frontend_as_a_ProblemDetails_it_may_read()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, SystemInfo);
        request.Headers.Add("Origin", TransportApiFactory.FrontendOrigin);

        using var response = await client.SendAsync(request);

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "unauthorized");
        response.Headers.GetValues(AllowOrigin).ShouldHaveSingleItem().ShouldBe(TransportApiFactory.FrontendOrigin);
    }

    [Fact]
    public async Task With_no_origin_configured_no_browser_on_another_origin_may_read_an_answer()
    {
        using var plain = new ApiFactory();
        using var client = plain.CreateClientFor(FixedIdentity.Random());
        using var preflight = Preflight(TransportApiFactory.FrontendOrigin, "POST", "authorization");
        using var request = new HttpRequestMessage(HttpMethod.Get, SystemInfo);
        request.Headers.Add("Origin", TransportApiFactory.FrontendOrigin);

        using var preflightResponse = await client.SendAsync(preflight);
        using var response = await client.SendAsync(request);

        preflightResponse.Headers.Contains(AllowOrigin).ShouldBeFalse();
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains(AllowOrigin).ShouldBeFalse();
    }

    private static HttpRequestMessage Preflight(string origin, string method, string? requestHeaders)
    {
        var preflight = new HttpRequestMessage(HttpMethod.Options, Bills);
        preflight.Headers.Add("Origin", origin);
        preflight.Headers.Add("Access-Control-Request-Method", method);
        if (requestHeaders is not null)
        {
            preflight.Headers.Add("Access-Control-Request-Headers", requestHeaders);
        }

        return preflight;
    }

    // The values of a header that lists names, split at the commas, whether it came as one value or several.
    private static string[] Listed(HttpResponseMessage response, string header)
        => [.. response.Headers.GetValues(header).SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))];
}
