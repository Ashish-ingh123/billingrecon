using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BillingRecon.Api.Common.Correlation;

namespace BillingRecon.IntegrationTests.Support;

internal static class HttpResponseExtensions
{
    /// <summary>
    /// Asserts everything the error contract promises (CLAUDE.md section 5) and returns the document for further checks:
    /// the status, <c>application/problem+json</c>, <c>type</c>, <c>title</c>, <c>errorCode</c>, <c>traceId</c> and a
    /// <c>correlationId</c> equal to the response header.
    /// </summary>
    public static async Task<JsonElement> ShouldBeProblemAsync(this HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        problem.GetProperty("status").GetInt32().ShouldBe((int)status);
        problem.GetProperty("type").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.GetProperty("errorCode").GetString().ShouldBe(errorCode);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.GetProperty("correlationId").GetGuid().ShouldBe(response.GetCorrelationId());

        return problem;
    }

    /// <summary>The correlation id from the response header; fails when it is missing, repeated or not a canonical GUID.</summary>
    public static Guid GetCorrelationId(this HttpResponseMessage response)
    {
        response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values).ShouldBeTrue("the response has no X-Correlation-Id header");
        var value = values.ShouldNotBeNull().ShouldHaveSingleItem();

        Guid.TryParseExact(value, "D", out var correlationId).ShouldBeTrue($"'{value}' is not a canonical GUID");
        correlationId.ShouldNotBe(Guid.Empty);

        return correlationId;
    }
}
