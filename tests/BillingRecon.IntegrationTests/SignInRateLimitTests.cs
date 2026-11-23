using System.Net;
using System.Net.Http.Json;
using BillingRecon.Api.Identity;
using BillingRecon.IntegrationTests.Support;
using Serilog.Events;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// The sign-in's limit per client address (ADR 0035; ADR 0031, point 12): <see cref="SignInLimits.AttemptsPerAddress"/>
/// sign-ins a minute from one address, however they end, then 429 <c>too_many_sign_ins</c> with <c>Retry-After</c>. The
/// address is the client's as a known proxy forwards it, and the connection's otherwise, so a stranger cannot spread its
/// sign-ins over invented addresses. Nothing else of the API is limited. Each test has a host of its own, so the windows
/// of one test do not reach another. The limit per account needs the database (<c>AuthApiTests</c>).
/// </summary>
public sealed class SignInRateLimitTests
{
    private static readonly Uri Login = new("/api/v1/auth/login", UriKind.Relative);

    [Fact]
    public async Task One_address_may_send_thirty_sign_ins_a_minute_and_the_next_is_answered_429_with_Retry_After()
    {
        using var factory = new TransportApiFactory();
        using var client = factory.CreateClient();

        // An empty sign-in is refused by its validation, without a database; it still counts, as every attempt does.
        for (var attempt = 0; attempt < SignInLimits.AttemptsPerAddress; attempt++)
        {
            using var counted = await SignInAsync(client, from: "203.0.113.7");
            counted.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using var limited = await SignInAsync(client, from: "203.0.113.7");
        using var otherAddress = await SignInAsync(client, from: "203.0.113.8");

        var problem = await limited.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "too_many_sign_ins");
        problem.GetProperty("detail").GetString().ShouldBe(SignInLimits.Detail);
        limited.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(TimeSpan.FromSeconds(60));
        limited.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        otherAddress.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_limit_is_logged_with_the_client_address_and_nothing_a_sign_in_typed()
    {
        using var factory = new TransportApiFactory();
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < SignInLimits.AttemptsPerAddress; attempt++)
        {
            using var counted = await SignInAsync(client, from: "203.0.113.9");
        }

        using var limited = await SignInAsync(client, from: "203.0.113.9");

        var warning = factory.Logs.ForCorrelationId(limited.GetCorrelationId())
            .Where(logEvent => logEvent.Level == LogEventLevel.Warning && logEvent.MessageTemplate.Text.StartsWith("Sign-ins from", StringComparison.Ordinal))
            .ShouldHaveSingleItem();
        warning.Scalar("ClientAddress").ShouldBe("203.0.113.9");
        warning.ToJson().ShouldNotContain("asha.rao", Case.Insensitive);
    }

    [Fact]
    public async Task Behind_a_known_proxy_the_limit_is_per_client_address_the_proxy_forwards()
    {
        using var factory = new TransportApiFactory();
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < SignInLimits.AttemptsPerAddress; attempt++)
        {
            using var counted = await SignInAsync(client, from: TransportApiFactory.ProxyAddress, forwardedFor: "198.51.100.4");
            counted.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using var limited = await SignInAsync(client, from: TransportApiFactory.ProxyAddress, forwardedFor: "198.51.100.4");
        using var anotherClient = await SignInAsync(client, from: TransportApiFactory.ProxyAddress, forwardedFor: "198.51.100.5");

        await limited.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "too_many_sign_ins");
        anotherClient.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_stranger_s_forwarded_addresses_are_not_believed_so_its_sign_ins_share_one_window()
    {
        using var factory = new TransportApiFactory();
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < SignInLimits.AttemptsPerAddress; attempt++)
        {
            using var counted = await SignInAsync(client, from: TransportApiFactory.StrangerAddress, forwardedFor: $"198.51.100.{attempt + 10}");
            counted.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using var limited = await SignInAsync(client, from: TransportApiFactory.StrangerAddress, forwardedFor: "198.51.100.99");

        await limited.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "too_many_sign_ins");
    }

    [Fact]
    public async Task Nothing_but_the_sign_in_is_limited()
    {
        using var factory = new TransportApiFactory();
        using var client = factory.CreateClientFor(FixedIdentity.Random());

        for (var attempt = 0; attempt <= 2 * SignInLimits.AttemptsPerAddress; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/system/info", UriKind.Relative));
            request.Headers.Add(RemoteAddressStartupFilter.HeaderName, "203.0.113.7");

            using var response = await client.SendAsync(request);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    private static Task<HttpResponseMessage> SignInAsync(HttpClient client, string from, string? forwardedFor = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Login) { Content = JsonContent.Create(new LoginRequest()) };
        request.Headers.Add(RemoteAddressStartupFilter.HeaderName, from);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
            request.Headers.Add("X-Forwarded-Proto", "https");
        }

        return client.SendAsync(request);
    }
}
