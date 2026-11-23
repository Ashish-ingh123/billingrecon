using BillingRecon.Api.Common.Correlation;
using BillingRecon.Api.Common.Idempotency;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace BillingRecon.Api.Common.Transport;

/// <summary>
/// How a request reaches the API and what its answer tells about the server (ADR 0035). TLS ends at a reverse proxy in
/// front of the API; the API believes the proxy's forwarded headers, answers only for its allowed hosts and, outside
/// Development, only over HTTPS, sends HSTS, lets the frontend's origins call it from a browser, and does not name its
/// server. The order of the middleware is in <c>Program.cs</c>.
/// </summary>
public static class TransportServiceCollectionExtensions
{
    /// <summary>One year: how long a browser keeps to HTTPS for the API's host after an answer with HSTS.</summary>
    public static readonly TimeSpan HstsMaxAge = TimeSpan.FromDays(365);

    /// <summary>How long a browser may keep the answer to a preflight request.</summary>
    public static readonly TimeSpan PreflightMaxAge = TimeSpan.FromMinutes(10);

    /// <summary>Adds the transport settings, each checked when the API starts.</summary>
    /// <param name="services">The service collection of the host.</param>
    /// <param name="environment">The environment of the host; outside Development the settings are stricter.</param>
    public static IServiceCollection AddTransport(this IServiceCollection services, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        AddForwardedHeaders(services);
        AddAllowedHosts(services);
        AddCors(services);

        // HSTS (used outside Development only, Program.cs): one year, this host only, not on the browsers' preload list.
        // The framework leaves out localhost, 127.0.0.1 and [::1].
        services.AddHsts(options =>
        {
            options.MaxAge = HstsMaxAge;
            options.IncludeSubDomains = false;
            options.Preload = false;
        });

        // Kestrel's "Server: Kestrel" tells a scanner what to try; nothing needs it.
        services.Configure<KestrelServerOptions>(options => options.AddServerHeader = false);

        return services;
    }

    private static void AddForwardedHeaders(IServiceCollection services)
    {
        services.AddOptions<ReverseProxyOptions>()
            .BindConfiguration(ReverseProxyOptions.SectionName)
            .Validate(ReverseProxyOptions.AreAddresses, ReverseProxyOptions.MalformedMessage)
            .Validate<IConfiguration>(
                (_, configuration) => ReverseProxyOptions.IsFrameworkSwitchOff(configuration[ReverseProxyOptions.FrameworkSwitchKey]),
                ReverseProxyOptions.FrameworkSwitchMessage)
            .ValidateOnStart();

        // The client's address and the scheme it used, as the proxy that ended TLS saw them, from that proxy only (the
        // loopback addresses, which the framework trusts by default, and the configured ones). One proxy deep: the last
        // entry of each header is the proxy's own. The Host header is the proxy's to pass on unchanged.
        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<ReverseProxyOptions>>((options, proxy) =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            foreach (var entry in proxy.Value.KnownProxies)
            {
                if (!ReverseProxyOptions.TryParse(entry, out var address, out var network))
                {
                    continue;
                }

                if (network is { } range)
                {
                    options.KnownIPNetworks.Add(range);
                }
                else
                {
                    options.KnownProxies.Add(address);
                }
            }
        });
    }

    private static void AddAllowedHosts(IServiceCollection services)
    {
        services.AddOptions<AllowedHostsOptions>()
            .Configure<IConfiguration>((options, configuration) => options.AllowedHosts = configuration[AllowedHostsOptions.Key])
            .Validate(AllowedHostsOptions.IsWellFormed, AllowedHostsOptions.MalformedMessage)
            .Validate<IHostEnvironment>(
                (options, host) => host.IsDevelopment() || AllowedHostsOptions.NamesTheHosts(options),
                AllowedHostsOptions.RequiredMessage)
            .ValidateOnStart();

        // The framework's own host filter reads the same key and runs before everything; TransportGuardMiddleware does
        // the check instead, so the framework's is told to let every host through.
        services.PostConfigure<HostFilteringOptions>(options => options.AllowedHosts = [AllowedHostsOptions.AnyHost]);
    }

    private static void AddCors(IServiceCollection services)
    {
        services.AddOptions<CorsOriginsOptions>()
            .BindConfiguration(CorsOriginsOptions.SectionName)
            .Validate(CorsOriginsOptions.AreOrigins, CorsOriginsOptions.MalformedMessage)
            .Validate<IHostEnvironment>(
                (options, host) => host.IsDevelopment() || CorsOriginsOptions.AreSecure(options),
                CorsOriginsOptions.InsecureMessage)
            .ValidateOnStart();

        services.AddCors();

        // No origin configured, no policy: a browser on another origin gets no CORS header and may not read an answer.
        // The token travels in a header, never a cookie, so no credentials are allowed.
        services.AddOptions<CorsOptions>().Configure<IOptions<CorsOriginsOptions>>((cors, origins) =>
        {
            if (origins.Value.Origins.Count == 0)
            {
                return;
            }

            cors.AddDefaultPolicy(policy => policy
                .WithOrigins([.. origins.Value.Origins])
                .WithMethods(HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch)
                .WithHeaders(HeaderNames.Authorization, HeaderNames.ContentType, HeaderNames.IfMatch, IdempotencyHeaders.Key, CorrelationIdMiddleware.HeaderName)
                .WithExposedHeaders(HeaderNames.ETag, HeaderNames.Location, HeaderNames.RetryAfter, CorrelationIdMiddleware.HeaderName, IdempotencyHeaders.Replayed)
                .SetPreflightMaxAge(PreflightMaxAge));
        });
    }
}
