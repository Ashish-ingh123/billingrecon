namespace BillingRecon.Infrastructure.Common.Settings;

/// <summary>
/// Where structured logs are shipped. Bound from the <c>Seq</c> configuration section and validated at startup.
/// Leaving <see cref="ServerUrl"/> empty turns the Seq sink off; the JSON console sink always stays on.
/// </summary>
public sealed class SeqOptions
{
    /// <summary>Name of the configuration section.</summary>
    public const string SectionName = "Seq";

    /// <summary>Message used when <see cref="ServerUrl"/> is set but unusable.</summary>
    public const string InvalidServerUrlMessage = "Seq:ServerUrl must be an absolute http or https URL, or empty to turn the Seq sink off.";

    /// <summary>Base URL of the Seq server, for example <c>http://localhost:5341</c>. Empty turns the sink off.</summary>
    public Uri? ServerUrl { get; init; }

    /// <summary>Optional ingestion API key. A secret: user-secrets or the <c>Seq__ApiKey</c> environment variable only.</summary>
    public string? ApiKey { get; init; }

    /// <summary>True when the sink is off, or the URL is absolute and uses http or https.</summary>
    public static bool HasValidServerUrl(SeqOptions options)
        => options.ServerUrl is null
            || (options.ServerUrl.IsAbsoluteUri && (options.ServerUrl.Scheme == Uri.UriSchemeHttp || options.ServerUrl.Scheme == Uri.UriSchemeHttps));
}
