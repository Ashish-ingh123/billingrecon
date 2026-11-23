using Microsoft.Extensions.Primitives;

namespace BillingRecon.Api.Common.Transport;

/// <summary>
/// The host names this API answers for (ADR 0035): the standard <c>AllowedHosts</c> key of ASP.NET Core, at the root of
/// the configuration, entries separated by semicolons. An entry is a host name (<c>api.hospital.example</c>), a
/// subdomain wildcard (<c>*.hospital.example</c>) or an IP address (<c>10.0.0.5</c>, <c>[2001:db8::5]</c>); the port of a
/// request does not matter. Outside Development the key is required and <c>*</c> is refused, so the API refuses to start
/// without it. In Development, unset or <c>*</c> means any host.
/// <para>
/// The check is <see cref="TransportGuardMiddleware"/>'s, not the framework's filter, which reads the same key: that one
/// would answer before the correlation id exists, with an HTML page, and would refuse the health probes too.
/// </para>
/// </summary>
public sealed class AllowedHostsOptions
{
    /// <summary>The configuration key, at the root.</summary>
    public const string Key = "AllowedHosts";

    /// <summary>The entry that allows any host, in Development only.</summary>
    public const string AnyHost = "*";

    /// <summary>Why the API does not start outside Development.</summary>
    public const string RequiredMessage =
        "AllowedHosts must name the host names this API answers for, separated by semicolons (for example "
        + "api.hospital.example or *.hospital.example); '*' is allowed in the Development environment only.";

    /// <summary>Why the API does not start with an entry it cannot match.</summary>
    public const string MalformedMessage =
        "AllowedHosts holds an entry that is not a host name, a '*.' subdomain wildcard or an IP address; write the host "
        + "alone, without a scheme, a port or a path, and an IPv6 address in brackets.";

    private string? _allowedHosts;
    private string[]? _entries;
    private StringSegment[]? _patterns;

    /// <summary>The value as configured, or null.</summary>
    public string? AllowedHosts
    {
        get => _allowedHosts;
        set
        {
            _allowedHosts = value;
            _entries = null;
            _patterns = null;
        }
    }

    /// <summary>The entries of <see cref="AllowedHosts"/>, trimmed, without the empty ones.</summary>
    public IReadOnlyList<string> Entries =>
        _entries ??= (AllowedHosts ?? string.Empty).Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>True when any host is allowed: nothing is configured, or <c>*</c> is.</summary>
    public bool AllowsAnyHost => Entries.Count == 0 || Entries.Contains(AnyHost);

    /// <summary>True when every entry is <c>*</c>, a host name, a <c>*.</c> wildcard or an IP address.</summary>
    public static bool IsWellFormed(AllowedHostsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Entries.All(entry => entry == AnyHost || IsHostPattern(entry));
    }

    /// <summary>True when the options name at least one host and do not allow any host.</summary>
    public static bool NamesTheHosts(AllowedHostsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return !options.AllowsAnyHost;
    }

    /// <summary>True when a request that names <paramref name="host"/> may be answered.</summary>
    public bool Allows(HostString host)
    {
        if (AllowsAnyHost)
        {
            return true;
        }

        if (!host.HasValue)
        {
            return false;
        }

        _patterns ??= [.. Entries.Select(entry => new StringSegment(entry))];
        try
        {
            // The framework's own matching: the port is dropped, case is ignored, "*.example.in" matches its subdomains.
            return HostString.MatchesAny(new StringSegment(host.Value), _patterns);
        }
        catch (FormatException)
        {
            // A port that is not a number. Kestrel refuses such a Host header itself; another server might not.
            return false;
        }
    }

    private static bool IsHostPattern(string entry)
    {
        if (entry.StartsWith("*.", StringComparison.Ordinal))
        {
            return Uri.CheckHostName(entry[2..]) == UriHostNameType.Dns;
        }

        return Uri.CheckHostName(entry) switch
        {
            UriHostNameType.Dns or UriHostNameType.IPv4 => true,

            // A Host header carries an IPv6 address in brackets, so the entry must too, or it would never match.
            UriHostNameType.IPv6 => entry.StartsWith('['),
            _ => false,
        };
    }
}
