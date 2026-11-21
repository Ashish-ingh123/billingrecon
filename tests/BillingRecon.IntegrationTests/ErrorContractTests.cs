using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BillingRecon.Api.Common.Correlation;
using BillingRecon.IntegrationTests.Support;
using Serilog.Events;

namespace BillingRecon.IntegrationTests;

/// <summary>The error contract of CLAUDE.md section 5, checked on the pipeline that ships (Production).</summary>
public sealed class ErrorContractTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Unhandled_exception_returns_500_problem_details_without_internals()
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());

        using var response = await client.GetAsync(new Uri("/api/v1/probe/throw", UriKind.Relative));

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.InternalServerError, "internal_error");
        problem.TryGetProperty("exception", out _).ShouldBeFalse();
        problem.TryGetProperty("detail", out _).ShouldBeFalse();

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("probe-secret");
        body.ShouldNotContain(nameof(InvalidOperationException));
        body.ShouldNotContain(nameof(ProbeController));
        body.ShouldNotContain("   at ");
    }

    [Theory]
    [InlineData("Validation", HttpStatusCode.BadRequest, "amount_invalid", "The amount is invalid.")]
    [InlineData("NotFound", HttpStatusCode.NotFound, "bill_not_found", "The bill does not exist.")]
    [InlineData("Unauthorized", HttpStatusCode.Unauthorized, "invalid_credentials", "The credentials are not right.")]
    [InlineData("Forbidden", HttpStatusCode.Forbidden, "checker_is_maker", "The checker must be a different user from the maker.")]
    [InlineData("Conflict", HttpStatusCode.Conflict, "concurrency_conflict", "The bill was changed by someone else.")]
    [InlineData("BusinessRule", HttpStatusCode.UnprocessableEntity, "bill_already_posted", "The bill is already posted.")]
    public async Task Business_error_maps_to_its_status_and_carries_its_code_and_message(
        string kind, HttpStatusCode status, string errorCode, string message)
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());

        using var response = await client.GetAsync(new Uri($"/api/v1/probe/errors/{kind}", UriKind.Relative));

        var problem = await response.ShouldBeProblemAsync(status, errorCode);
        problem.GetProperty("detail").GetString().ShouldBe(message);
    }

    [Fact]
    public async Task Validation_error_lists_messages_per_field()
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());

        using var response = await client.GetAsync(new Uri("/api/v1/probe/errors/Validation", UriKind.Relative));

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "amount_invalid");
        problem.GetProperty("errors").GetProperty("amount")[0].GetString().ShouldBe("Must be greater than zero.");
    }

    [Fact]
    public async Task Model_binding_failure_returns_validation_failed_with_field_errors()
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());

        using var response = await client.GetAsync(new Uri("/api/v1/probe/validate?quantity=9", UriKind.Relative));

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        problem.GetProperty("errors").GetProperty("quantity").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Status_result_from_a_controller_returns_problem_details()
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());

        using var response = await client.GetAsync(new Uri("/api/v1/probe/missing", UriKind.Relative));

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details()
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());

        using var response = await client.GetAsync(new Uri("/api/v1/does-not-exist", UriKind.Relative));

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Wrong_http_method_returns_problem_details()
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());

        using var response = await client.PostAsync(new Uri("/api/v1/probe/missing", UriKind.Relative), content: null);

        await response.ShouldBeProblemAsync(HttpStatusCode.MethodNotAllowed, "method_not_allowed");
    }

    [Theory]
    [InlineData(400, "bad_request")]
    [InlineData(408, "request_timeout")]
    [InlineData(413, "payload_too_large")]
    public async Task A_request_the_server_refuses_to_read_keeps_its_own_status_and_is_not_logged_as_an_error(int statusCode, string errorCode)
    {
        var correlationId = Guid.NewGuid();
        using var client = factory.CreateClientFor(FixedIdentity.Random());
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/v1/probe/rejected/{statusCode}", UriKind.Relative));
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId.ToString());

        using var response = await client.SendAsync(request);

        await response.ShouldBeProblemAsync((HttpStatusCode)statusCode, errorCode);
        var events = factory.Logs.ForCorrelationId(correlationId);
        events.ShouldNotBeEmpty();
        events.ShouldAllBe(logEvent => logEvent.Level < LogEventLevel.Error && logEvent.Exception == null);
    }

    [Theory]
    [InlineData(428, "precondition_required", "Precondition Required")]
    [InlineData(429, "too_many_requests", "Too Many Requests")]
    public async Task A_status_the_framework_has_no_defaults_for_still_gets_type_title_and_code(int statusCode, string errorCode, string title)
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());

        using var response = await client.GetAsync(new Uri($"/api/v1/probe/status/{statusCode}", UriKind.Relative));

        var problem = await response.ShouldBeProblemAsync((HttpStatusCode)statusCode, errorCode);
        problem.GetProperty("title").GetString().ShouldBe(title);
        problem.GetProperty("type").GetString().ShouldBe("about:blank");
    }

    [Fact]
    public async Task A_validation_error_with_more_than_200_fields_is_returned_complete()
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());

        using var response = await client.GetAsync(new Uri("/api/v1/probe/many-field-errors", UriKind.Relative));

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "lines_invalid");
        problem.GetProperty("errors").EnumerateObject().Count().ShouldBe(ProbeController.ManyFieldErrorsCount);
    }

    [Theory]
    [InlineData("{\"amount\":\"abc\"}")]
    [InlineData("{}")]
    [InlineData("{\"amount\":")]
    [InlineData("not json")]
    public async Task A_body_that_cannot_be_read_is_a_validation_error_without_serializer_internals(string json)
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(new Uri("/api/v1/probe/echo", UriKind.Relative), content);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        problem.GetProperty("errors").EnumerateObject().ShouldNotBeEmpty();

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("System.");
        body.ShouldNotContain("BillingRecon");
        body.ShouldNotContain("LineNumber");
        body.ShouldNotContain("BytePositionInLine");
    }

    [Fact]
    public async Task A_well_formed_body_is_accepted()
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());
        using var content = new StringContent("{\"amount\":12.50}", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(new Uri("/api/v1/probe/echo", UriKind.Relative), content);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("12.50");
    }

    [Theory]
    [InlineData("/api/v1/probe/throw", HttpStatusCode.InternalServerError, "internal_error")]
    [InlineData("/api/v1/does-not-exist", HttpStatusCode.NotFound, "not_found")]
    [InlineData("/api/v1/probe/errors/Conflict", HttpStatusCode.Conflict, "concurrency_conflict")]
    public async Task Errors_are_problem_details_even_when_the_client_does_not_accept_json(
        string path, HttpStatusCode status, string errorCode)
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

        using var response = await client.SendAsync(request);

        await response.ShouldBeProblemAsync(status, errorCode);
    }
}
