namespace BillingRecon.Api.Common.Transport;

/// <summary>
/// The origins of the frontend that a browser may call this API from (ADR 0035): <c>Cors:Origins</c>, each written as a
/// browser sends it (<c>https://app.hospital.example</c>, a port only when it is not the default). None by default: when
/// the proxy serves the frontend and the API from one origin, no cross-origin call is needed. Checked when the API
/// starts: no <c>*</c>, no path, and <c>https</c> outside Development.
/// </summary>
public sealed class CorsOriginsOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Cors";

    /// <summary>Why the API does not start with an entry that is not an origin.</summary>
    public const string MalformedMessage =
        "Cors:Origins must list origins as a browser sends them, a scheme, a host and a port only when it is not the "
        + "default (for example https://app.hospital.example), with no '*', no path and no trailing slash.";

    /// <summary>Why the API does not start outside Development with an origin that is not HTTPS.</summary>
    public const string InsecureMessage =
        "Cors:Origins may name an http origin in the Development environment only; elsewhere every origin is https.";

    /// <summary>The origins, as configured.</summary>
    public IReadOnlyList<string> Origins { get; set; } = [];

    /// <summary>True when every entry of <see cref="Origins"/> is an origin.</summary>
    public static bool AreOrigins(CorsOriginsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Origins.All(IsOrigin);
    }

    /// <summary>True when every origin of <see cref="Origins"/> is https; an entry that is no origin is <see cref="AreOrigins"/>'s.</summary>
    public static bool AreSecure(CorsOriginsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Origins.All(origin => !IsOrigin(origin) || origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsOrigin(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && !value.Contains('*', StringComparison.Ordinal)
            && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && uri.UserInfo.Length == 0
            && string.Equals(value, uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
}
