using Microsoft.Extensions.Hosting;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// Hosts the API as a deployment behind its proxy is configured (ADR 0035): the host names it answers for, with a
/// subdomain wildcard; a known proxy by address and one by range; the frontend's origin. Production unless a test names
/// another environment.
/// </summary>
public sealed class TransportApiFactory : ApiFactory
{
    /// <summary>The host name a deployment answers for, which unlike localhost gets HSTS.</summary>
    public const string NamedHost = "api.billingrecon.test";

    /// <summary>A host the wildcard entry allows.</summary>
    public const string WildcardHost = "east.wards.billingrecon.test";

    /// <summary>The proxy that ends TLS, named by its address.</summary>
    public const string ProxyAddress = "10.0.0.2";

    /// <summary>An address inside the range of known proxies.</summary>
    public const string ProxyInRange = "10.1.4.4";

    /// <summary>An address that is no known proxy.</summary>
    public const string StrangerAddress = "192.0.2.10";

    /// <summary>The frontend's origin.</summary>
    public const string FrontendOrigin = "https://app.billingrecon.test";

    /// <summary>The value of AllowedHosts.</summary>
    public const string AllowedHosts = $"{Host};{NamedHost};*.wards.billingrecon.test";

    private readonly string _environment;

    /// <summary>In Production. The one public constructor, as a class fixture must have.</summary>
    public TransportApiFactory()
        : this(Environments.Production)
    {
    }

    /// <summary>In <paramref name="environment"/>.</summary>
    internal TransportApiFactory(string environment)
    {
        _environment = environment;
    }

    protected override string EnvironmentName => _environment;

    protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
    [
        .. base.Settings,
        new(AllowedHostsSetting, AllowedHosts),
        new("ReverseProxy:KnownProxies:0", ProxyAddress),
        new("ReverseProxy:KnownProxies:1", "10.1.0.0/16"),
        new("Cors:Origins:0", FrontendOrigin),
    ];
}
