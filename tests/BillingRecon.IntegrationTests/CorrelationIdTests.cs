using System.Net;
using BillingRecon.Api.Common.Correlation;
using BillingRecon.IntegrationTests.Support;

namespace BillingRecon.IntegrationTests;

/// <summary>BR-703: every request has a correlation id, accepted from X-Correlation-Id or generated, and returned in the response.</summary>
public sealed class CorrelationIdTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string AnyPath = "/api/v1/system/info";

    [Fact]
    public async Task A_supplied_correlation_id_round_trips()
    {
        var supplied = Guid.NewGuid();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, AnyPath, supplied.ToString());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.GetCorrelationId().ShouldBe(supplied);
    }

    [Fact]
    public async Task A_supplied_correlation_id_is_returned_in_canonical_lower_case()
    {
        var supplied = Guid.NewGuid();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, AnyPath, supplied.ToString().ToUpperInvariant());

        response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).ShouldHaveSingleItem().ShouldBe(supplied.ToString());
    }

    [Fact]
    public async Task A_request_without_a_correlation_id_gets_a_new_one_each_time()
    {
        using var client = factory.CreateClient();

        using var first = await client.GetAsync(new Uri(AnyPath, UriKind.Relative));
        using var second = await client.GetAsync(new Uri(AnyPath, UriKind.Relative));

        first.GetCorrelationId().ShouldNotBe(second.GetCorrelationId());
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("12345")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("{3f2504e0-4f89-11d3-9a0c-0305e82c3301}")]
    [InlineData("3f2504e04f8911d39a0c0305e82c3301")]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301, 3f2504e0-4f89-11d3-9a0c-0305e82c3302")]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301<script>alert(1)</script>")]
    public async Task A_malformed_correlation_id_is_replaced_not_echoed(string supplied)
    {
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, AnyPath, supplied);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var returned = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).ShouldHaveSingleItem();
        returned.ShouldNotBe(supplied);
        response.GetCorrelationId().ShouldNotBe(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301"));
    }

    [Theory]
    [InlineData("/api/v1/probe/throw", HttpStatusCode.InternalServerError, "internal_error")]
    [InlineData("/api/v1/does-not-exist", HttpStatusCode.NotFound, "not_found")]
    [InlineData("/api/v1/probe/errors/BusinessRule", HttpStatusCode.UnprocessableEntity, "bill_already_posted")]
    public async Task The_correlation_id_round_trips_on_error_responses_in_the_header_and_the_body(
        string path, HttpStatusCode status, string errorCode)
    {
        var supplied = Guid.NewGuid();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, path, supplied.ToString());

        var problem = await response.ShouldBeProblemAsync(status, errorCode);
        response.GetCorrelationId().ShouldBe(supplied);
        problem.GetProperty("correlationId").GetGuid().ShouldBe(supplied);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string correlationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, correlationId).ShouldBeTrue();

        return await client.SendAsync(request);
    }
}
