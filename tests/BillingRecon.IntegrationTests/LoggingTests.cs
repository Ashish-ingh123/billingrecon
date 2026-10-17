using System.Net;
using BillingRecon.Api.Common.Correlation;
using BillingRecon.Api.Common.Errors;
using BillingRecon.IntegrationTests.Support;
using Serilog.Events;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// What reaches the log sinks: every event of a request carries its correlation id (BR-703), tenant and user appear
/// when the request is authenticated, phone numbers and e-mail addresses are masked, and an unhandled exception is
/// logged exactly once.
/// </summary>
public sealed class LoggingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ProbeMessage = "Probe contact";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task The_events_of_a_request_carry_its_correlation_id()
    {
        var correlationId = Guid.NewGuid();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, "/api/v1/probe/log", correlationId);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var events = factory.Logs.ForCorrelationId(correlationId);
        events.ShouldContain(logEvent => logEvent.MessageTemplate.Text.StartsWith(ProbeMessage, StringComparison.Ordinal));
        events.ShouldContain(logEvent => logEvent.IsRequestCompletion());
    }

    [Fact]
    public async Task An_unhandled_exception_is_logged_exactly_once_with_the_correlation_id()
    {
        var correlationId = Guid.NewGuid();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, "/api/v1/probe/throw", correlationId);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var events = factory.Logs.ForCorrelationId(correlationId);

        var failure = events.Where(logEvent => logEvent.Exception is not null).ShouldHaveSingleItem();
        failure.Level.ShouldBe(LogEventLevel.Error);
        failure.Exception.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe(ProbeController.SecretExceptionMessage);
        failure.Scalar("SourceContext").ShouldBe(typeof(GlobalExceptionHandler).FullName);

        var completion = events.Where(logEvent => logEvent.IsRequestCompletion()).ShouldHaveSingleItem();
        completion.Level.ShouldBe(LogEventLevel.Error);
        completion.Scalar("StatusCode").ShouldBe(500);

        factory.Logs.Events.ShouldNotContain(logEvent =>
            Equals(logEvent.Scalar("SourceContext"), "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware"));
    }

    [Theory]
    [InlineData("/api/v1/probe/log")]
    [InlineData("/api/v1/probe/throw")]
    public async Task Tenant_and_user_are_on_every_event_of_an_authenticated_request(string path)
    {
        var correlationId = Guid.NewGuid();
        var tenantId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();
        using var client = factory.CreateClient();
        using var request = NewRequest(path, correlationId);
        request.Headers.Add(TestPrincipalStartupFilter.TenantHeader, tenantId);
        request.Headers.Add(TestPrincipalStartupFilter.UserHeader, userId);

        using var response = await client.SendAsync(request);

        var events = factory.Logs.ForCorrelationId(correlationId);
        events.Count.ShouldBeGreaterThanOrEqualTo(2);
        events.ShouldAllBe(logEvent => Equals(logEvent.Scalar("TenantId"), tenantId) && Equals(logEvent.Scalar("UserId"), userId));
    }

    [Fact]
    public async Task An_anonymous_request_has_no_tenant_or_user_on_its_events()
    {
        var correlationId = Guid.NewGuid();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, "/api/v1/probe/log", correlationId);

        var events = factory.Logs.ForCorrelationId(correlationId);
        events.ShouldNotBeEmpty();
        events.ShouldAllBe(logEvent => !logEvent.Properties.ContainsKey("TenantId") && !logEvent.Properties.ContainsKey("UserId"));
    }

    [Fact]
    public async Task Phone_numbers_and_email_addresses_are_masked_before_they_reach_a_sink()
    {
        var correlationId = Guid.NewGuid();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, "/api/v1/probe/log", correlationId);

        var events = factory.Logs.ForCorrelationId(correlationId);
        var probe = events.Where(logEvent => logEvent.MessageTemplate.Text.StartsWith(ProbeMessage, StringComparison.Ordinal)).ShouldHaveSingleItem();
        probe.Scalar("Email").ShouldBe("p***@example.com");
        probe.Scalar("Phone").ShouldBe("******3210");
        probe.Scalar("Note").ShouldBe("call ******3210 or write to p***@example.com");

        var patient = probe.Properties["Patient"].ShouldBeOfType<StructureValue>();
        Property(patient, "Name").ShouldBeOfType<ScalarValue>().Value.ShouldBe("Priya");
        Property(patient, "Mobile").ShouldBeOfType<ScalarValue>().Value.ShouldBe("***");
        Property(Property(patient, "Guarantor").ShouldBeOfType<StructureValue>(), "EmailAddress")
            .ShouldBeOfType<ScalarValue>().Value.ShouldBe("p***@example.com");

        foreach (var json in events.Select(logEvent => logEvent.ToJson()))
        {
            json.ShouldNotContain("priya.sharma");
            json.ShouldNotContain("98765 43210");
            json.ShouldNotContain("9876543210");
        }
    }

    [Fact]
    public async Task The_request_log_leaves_out_the_query_string()
    {
        var correlationId = Guid.NewGuid();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, "/api/v1/probe/validate?quantity=3&contact=priya.sharma%40example.com", correlationId);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var completion = factory.Logs.ForCorrelationId(correlationId).Where(logEvent => logEvent.IsRequestCompletion()).ShouldHaveSingleItem();
        completion.Scalar("RequestPath").ShouldBe("/api/v1/probe/validate");
        completion.ToJson().ShouldNotContain("priya");
    }

    [Fact]
    public async Task A_healthy_probe_is_not_logged_but_a_failing_one_is()
    {
        var liveId = Guid.NewGuid();
        var readyId = Guid.NewGuid();
        using var client = factory.CreateClient();

        using var live = await SendAsync(client, "/health/live", liveId);
        using var ready = await SendAsync(client, "/health/ready", readyId);

        live.StatusCode.ShouldBe(HttpStatusCode.OK);
        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        factory.Logs.ForCorrelationId(liveId).ShouldNotContain(logEvent => logEvent.IsRequestCompletion());
        factory.Logs.ForCorrelationId(readyId).Where(logEvent => logEvent.IsRequestCompletion()).ShouldHaveSingleItem().Level.ShouldBe(LogEventLevel.Error);
    }

    [Fact]
    public async Task A_request_abandoned_by_the_client_is_answered_499_and_is_not_logged_as_an_error()
    {
        var correlationId = Guid.NewGuid();
        using var client = factory.CreateClient();
        using var clientGivesUp = new CancellationTokenSource();
        using var request = NewRequest("/api/v1/probe/wait", correlationId);

        var pending = client.SendAsync(request, clientGivesUp.Token);
        await factory.Logs.WaitForAsync(correlationId, logEvent => logEvent.MessageTemplate.Text == ProbeController.WaitingMessage, Timeout);
        await clientGivesUp.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => pending);

        var completion = await factory.Logs.WaitForAsync(correlationId, logEvent => logEvent.IsRequestCompletion(), Timeout);
        completion.Scalar("StatusCode").ShouldBe(499);
        completion.Level.ShouldBe(LogEventLevel.Information);
        factory.Logs.ForCorrelationId(correlationId).ShouldAllBe(logEvent => logEvent.Level < LogEventLevel.Error && logEvent.Exception == null);
    }

    private static LogEventPropertyValue Property(StructureValue structure, string name)
        => structure.Properties.Where(property => property.Name == name).ShouldHaveSingleItem().Value;

    private static HttpRequestMessage NewRequest(string path, Guid correlationId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId.ToString());

        return request;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, Guid correlationId)
    {
        using var request = NewRequest(path, correlationId);

        return await client.SendAsync(request);
    }
}
