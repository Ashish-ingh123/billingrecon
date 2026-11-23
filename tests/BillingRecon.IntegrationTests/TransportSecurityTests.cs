using System.Net;
using System.Net.Http.Json;
using BillingRecon.Api.Common.Transport;
using BillingRecon.Api.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// How a request reaches the API (ADR 0035): TLS ends at a proxy, whose forwarded headers the API believes from a known
/// proxy only; outside Development nothing but HTTPS is answered, with no redirect, and an answer over HTTPS carries HSTS;
/// a host the API does not answer for is refused; the health endpoints answer probes by any name and over plain HTTP.
/// Every refusal is a ProblemDetails, made before the request is read, a token included.
/// </summary>
public sealed class TransportSecurityTests(TransportApiFactory factory) : IClassFixture<TransportApiFactory>
{
    private const string Hsts = "Strict-Transport-Security";

    private static readonly Uri SystemInfo = new("/api/v1/system/info", UriKind.Relative);

    [Fact]
    public async Task Outside_development_a_request_over_plain_http_is_refused_with_no_redirect_whatever_it_carries()
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());
        client.BaseAddress = new Uri("http://localhost");

        using var response = await client.GetAsync(SystemInfo);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "https_required");
        problem.GetProperty("detail").GetString().ShouldBe(TransportGuardMiddleware.HttpsRequiredDetail);
        response.Headers.Location.ShouldBeNull();
        response.Headers.Contains(Hsts).ShouldBeFalse();
    }

    [Fact]
    public async Task A_sign_in_over_plain_http_is_refused_before_it_is_read()
    {
        using var client = factory.CreateClient();
        client.BaseAddress = new Uri("http://localhost");

        // A well-formed sign-in would reach the database, which this host cannot reach: a 500. It never gets that far.
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginRequest { Tenant = "city-hospital", Email = "asha.rao@example.in", Password = "correct horse battery staple" });

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "https_required");
    }

    [Theory]
    [InlineData("http://localhost/health/live", HttpStatusCode.OK)]
    [InlineData("http://10.9.8.7:8080/health/live", HttpStatusCode.OK)]
    [InlineData("https://a-name-nobody-listed.example/health/live", HttpStatusCode.OK)]
    [InlineData("http://10.9.8.7:8080/health/ready", HttpStatusCode.ServiceUnavailable)]
    public async Task The_health_endpoints_answer_probes_over_plain_http_by_whatever_name_reaches_the_container(string url, HttpStatusCode status)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(url));

        // Ready reports the unreachable database of this host; either way the probe is answered, not refused.
        response.StatusCode.ShouldBe(status);
    }

    [Fact]
    public async Task An_answer_over_https_for_a_named_host_carries_HSTS_for_one_year_and_this_host_only()
    {
        using var signedIn = factory.CreateClientFor(FixedIdentity.Random());
        signedIn.BaseAddress = new Uri($"https://{TransportApiFactory.NamedHost}");
        using var anonymous = factory.CreateClient();
        anonymous.BaseAddress = signedIn.BaseAddress;

        using var answered = await signedIn.GetAsync(SystemInfo);
        using var refused = await anonymous.GetAsync(SystemInfo);

        answered.StatusCode.ShouldBe(HttpStatusCode.OK);
        answered.Headers.GetValues(Hsts).ShouldHaveSingleItem().ShouldBe("max-age=31536000");
        await refused.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "unauthorized");
        refused.Headers.GetValues(Hsts).ShouldHaveSingleItem().ShouldBe("max-age=31536000");
    }

    [Fact]
    public async Task Localhost_gets_no_HSTS_so_a_developer_s_browser_is_not_bound_to_HTTPS_for_it()
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());

        using var response = await client.GetAsync(SystemInfo);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains(Hsts).ShouldBeFalse();
    }

    [Fact]
    public async Task In_development_plain_http_is_answered_and_no_answer_carries_HSTS()
    {
        using var development = new TransportApiFactory(Environments.Development);
        using var client = development.CreateClient();

        // In Development a request without a token acts as the development user (ADR 0031).
        using var overHttp = await client.GetAsync(new Uri("http://localhost/api/v1/system/info"));
        using var overHttps = await client.GetAsync(new Uri($"https://{TransportApiFactory.NamedHost}/api/v1/system/info"));

        overHttp.StatusCode.ShouldBe(HttpStatusCode.OK);
        overHttps.StatusCode.ShouldBe(HttpStatusCode.OK);
        overHttps.Headers.Contains(Hsts).ShouldBeFalse();
    }

    [Theory]
    [InlineData(TransportApiFactory.ProxyAddress)]
    [InlineData(TransportApiFactory.ProxyInRange)]
    [InlineData("127.0.0.1")]
    public async Task HTTPS_ended_by_a_known_proxy_counts_as_HTTPS(string proxy)
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());
        using var request = ForwardedRequest(proxy, "https");

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues(Hsts).ShouldHaveSingleItem().ShouldBe("max-age=31536000");
    }

    [Fact]
    public async Task Forwarded_headers_from_an_address_that_is_no_known_proxy_are_not_believed()
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());
        using var request = ForwardedRequest(TransportApiFactory.StrangerAddress, "https");

        using var response = await client.SendAsync(request);

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "https_required");
    }

    [Fact]
    public async Task A_known_proxy_that_forwards_plain_http_gets_it_refused()
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());
        using var request = ForwardedRequest(TransportApiFactory.ProxyAddress, "http");

        using var response = await client.SendAsync(request);

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "https_required");
    }

    [Theory]
    [InlineData("evil.example")]
    [InlineData("billingrecon.test")]
    [InlineData("wards.billingrecon.test")]
    [InlineData("api.billingrecon.test.evil.example")]
    [InlineData("10.0.0.2")]
    public async Task A_host_the_API_does_not_answer_for_is_refused_before_the_token_is_asked_for(string host)
    {
        using var client = factory.CreateClient();
        client.BaseAddress = new Uri($"https://{host}");

        using var response = await client.GetAsync(SystemInfo);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "host_not_allowed");
        problem.GetProperty("detail").GetString().ShouldBe(TransportGuardMiddleware.HostNotAllowedDetail);
    }

    [Theory]
    [InlineData("API.BillingRecon.Test")]
    [InlineData("api.billingrecon.test:8443")]
    [InlineData(TransportApiFactory.WildcardHost)]
    [InlineData("localhost")]
    public async Task Every_allowed_host_is_answered_in_any_case_and_on_any_port(string host)
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());
        using var request = new HttpRequestMessage(HttpMethod.Get, SystemInfo);
        request.Headers.Host = host;

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public void The_framework_s_own_host_filter_lets_every_host_through_to_the_API_s_check()
    {
        var options = factory.Services.GetRequiredService<IOptions<HostFilteringOptions>>().Value;

        options.AllowedHosts.ShouldBe([AllowedHostsOptions.AnyHost]);
    }

    [Fact]
    public void The_known_proxies_reach_the_framework_besides_the_loopback_it_trusts_by_default()
    {
        var options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        options.ForwardedHeaders.ShouldBe(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
        options.ForwardLimit.ShouldBe(1);
        options.KnownProxies.ShouldContain(IPAddress.Parse(TransportApiFactory.ProxyAddress));
        options.KnownProxies.ShouldContain(IPAddress.IPv6Loopback);
        options.KnownIPNetworks.ShouldContain(System.Net.IPNetwork.Parse("10.1.0.0/16"));
        options.KnownIPNetworks.ShouldContain(System.Net.IPNetwork.Parse("127.0.0.0/8"));
    }

    // A request as the proxy sends it on: plain HTTP to the API, the scheme and the client's address in the headers.
    private static HttpRequestMessage ForwardedRequest(string proxy, string scheme)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"http://{TransportApiFactory.NamedHost}/api/v1/system/info"));
        request.Headers.Add(RemoteAddressStartupFilter.HeaderName, proxy);
        request.Headers.Add("X-Forwarded-Proto", scheme);
        request.Headers.Add("X-Forwarded-For", "198.51.100.4");

        return request;
    }
}
