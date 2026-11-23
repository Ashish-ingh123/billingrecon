using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace BillingRecon.Api.Common.Transport;

/// <summary>
/// The reverse proxy that ends TLS in front of the API (ADR 0035): <c>ReverseProxy:KnownProxies</c>, the addresses
/// (<c>10.0.0.5</c>) or ranges (<c>10.0.0.0/24</c>) whose <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> the API
/// believes, besides the loopback addresses the framework trusts by default. A request from any other address keeps its
/// own scheme and address, whatever headers it sends. Checked when the API starts.
/// </summary>
public sealed class ReverseProxyOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "ReverseProxy";

    /// <summary>
    /// The framework's switch (<c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c>, read as this key): it adds a second reader of the
    /// forwarded headers in front of everything, which believes them from any address.
    /// </summary>
    public const string FrameworkSwitchKey = "FORWARDEDHEADERS_ENABLED";

    /// <summary>Why the API does not start with an entry that is not an address or a range.</summary>
    public const string MalformedMessage =
        "ReverseProxy:KnownProxies must list IP addresses or CIDR ranges of the proxy in front of the API (for example "
        + "10.0.0.5 or 10.0.0.0/24).";

    /// <summary>Why the API does not start with the framework's switch on.</summary>
    public const string FrameworkSwitchMessage =
        "ASPNETCORE_FORWARDEDHEADERS_ENABLED makes the API believe forwarded headers from any address; leave it unset and "
        + "list the proxy in ReverseProxy:KnownProxies instead.";

    /// <summary>The addresses and ranges of the proxy, as configured.</summary>
    public IReadOnlyList<string> KnownProxies { get; set; } = [];

    /// <summary>True when every entry of <see cref="KnownProxies"/> is an IP address or a CIDR range.</summary>
    public static bool AreAddresses(ReverseProxyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.KnownProxies.All(entry => TryParse(entry, out _, out _));
    }

    /// <summary>True unless the framework's switch is on.</summary>
    public static bool IsFrameworkSwitchOff(string? value) => !string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads one entry: an address, or a range.</summary>
    public static bool TryParse(string? entry, [NotNullWhen(true)] out IPAddress? address, out IPNetwork? network)
    {
        address = null;
        network = null;
        var text = entry?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (text.Contains('/', StringComparison.Ordinal))
        {
            if (!IPNetwork.TryParse(text, out var range))
            {
                return false;
            }

            network = range;
            address = range.BaseAddress;
            return true;
        }

        return IPAddress.TryParse(text, out address);
    }
}
