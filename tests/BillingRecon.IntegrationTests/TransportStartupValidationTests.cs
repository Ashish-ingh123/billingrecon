using System.Net;
using BillingRecon.Api.Common.Transport;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// The transport settings are checked when the API starts (ADR 0035): outside Development it must be told the hosts it
/// answers for; it refuses a host, a proxy or an origin it could never match, an http origin outside Development, and the
/// framework's switch that would believe forwarded headers from anyone.
/// </summary>
public sealed class TransportStartupValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ; ")]
    [InlineData("*")]
    [InlineData("api.hospital.example;*")]
    public void Outside_development_the_api_refuses_to_start_without_the_hosts_it_answers_for(string allowedHosts)
    {
        using var factory = new SettingsApiFactory(Environments.Production, (ApiFactory.AllowedHostsSetting, allowedHosts));

        var exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        exception.Message.ShouldBe(AllowedHostsOptions.RequiredMessage);
    }

    [Theory]
    [InlineData("https://api.hospital.example")]
    [InlineData("api.hospital.example:443")]
    [InlineData("api.hospital.example/billing")]
    [InlineData("api.hospital.example;api hospital.example")]
    [InlineData("::1")]
    [InlineData("*hospital.example")]
    [InlineData("api.*.example")]
    public void The_api_refuses_to_start_with_a_host_it_could_never_match(string allowedHosts)
    {
        using var factory = new SettingsApiFactory(Environments.Production, (ApiFactory.AllowedHostsSetting, allowedHosts));

        var exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        exception.Message.ShouldBe(AllowedHostsOptions.MalformedMessage);
    }

    [Theory]
    [InlineData("api.hospital.example")]
    [InlineData("*.hospital.example;api.hospital.example")]
    [InlineData("10.0.0.5;[2001:db8::5];localhost")]
    public void Outside_development_the_api_starts_with_host_names_wildcards_and_addresses(string allowedHosts)
    {
        using var factory = new SettingsApiFactory(Environments.Production, (ApiFactory.AllowedHostsSetting, allowedHosts));

        factory.Services.GetRequiredService<IOptions<AllowedHostsOptions>>().Value.AllowsAnyHost.ShouldBeFalse();
    }

    [Fact]
    public async Task In_development_any_host_is_answered_when_none_is_named()
    {
        using var factory = new DevelopmentApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("http://a-name-nobody-listed.example/api/v1/system/info"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("10.0.0.0/33")]
    [InlineData("10.0.0.0/")]
    [InlineData("https://10.0.0.5")]
    [InlineData("10.0.0.5:8080")]
    public void The_api_refuses_to_start_with_a_proxy_that_is_not_an_address_or_a_range(string proxy)
    {
        using var factory = new SettingsApiFactory(Environments.Production, ("ReverseProxy:KnownProxies:0", proxy));

        var exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        exception.Message.ShouldBe(ReverseProxyOptions.MalformedMessage);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    public void The_api_refuses_to_start_with_the_framework_s_switch_that_believes_forwarded_headers_from_anyone(string value)
    {
        using var factory = new SettingsApiFactory(Environments.Production, (ReverseProxyOptions.FrameworkSwitchKey, value));

        var exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        exception.Message.ShouldBe(ReverseProxyOptions.FrameworkSwitchMessage);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.hospital.example")]
    [InlineData("https://app.hospital.example/")]
    [InlineData("https://app.hospital.example/billing")]
    [InlineData("https://app.hospital.example:443")]
    [InlineData("app.hospital.example")]
    [InlineData("ftp://app.hospital.example")]
    [InlineData("https://user@app.hospital.example")]
    public void The_api_refuses_to_start_with_an_origin_a_browser_never_sends(string origin)
    {
        using var factory = new SettingsApiFactory(Environments.Production, ("Cors:Origins:0", origin));

        var exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        exception.Message.ShouldBe(CorsOriginsOptions.MalformedMessage);
    }

    [Fact]
    public void Outside_development_the_api_refuses_to_start_with_an_http_origin()
    {
        using var factory = new SettingsApiFactory(Environments.Production, ("Cors:Origins:0", "http://app.hospital.example"));

        var exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        exception.Message.ShouldBe(CorsOriginsOptions.InsecureMessage);
    }

    [Fact]
    public void In_development_the_api_starts_with_the_origin_of_the_vite_dev_server_it_ships()
    {
        using var factory = new DevelopmentApiFactory();

        factory.Services.GetRequiredService<IOptions<CorsOriginsOptions>>().Value.Origins.ShouldBe(["http://localhost:5173"]);
    }

    [Fact]
    public void Kestrel_names_no_server_and_HSTS_lasts_one_year_for_this_host_only()
    {
        using var factory = new ApiFactory();

        factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.AddServerHeader.ShouldBeFalse();
        var hsts = factory.Services.GetRequiredService<IOptions<HstsOptions>>().Value;
        (hsts.MaxAge, hsts.IncludeSubDomains, hsts.Preload).ShouldBe((TimeSpan.FromDays(365), false, false));
    }

    private sealed class SettingsApiFactory(string environment, params (string Key, string? Value)[] settings) : ApiFactory
    {
        protected override string EnvironmentName => environment;

        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [
            .. base.Settings,
            .. settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)),
        ];
    }
}
