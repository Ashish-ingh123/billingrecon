using System.Net.Mail;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// The mail server a forgotten password is answered through (ADR 0061, amended): <c>Mail:Host</c> and what goes with
/// it, from the host's secrets. With no host the product sends no mail at all, no link is ever made, and an
/// administrator's reset stays the only way; that is a supported way to run it, not a misconfiguration.
/// </summary>
public sealed class MailOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Mail";

    /// <summary>The message of a server that is named without what a mail needs.</summary>
    public const string IncompleteMessage =
        "Mail:Host is set, so Mail:From (the address the mails come from) and Mail:PublicBaseUrl (the address users open the app at, https outside Development) are required, and Mail:Port is between 1 and 65535.";

    /// <summary>The SMTP server; empty for none.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Its port: 587, the submission port, unless set.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Whether the connection is encrypted (STARTTLS). True unless a setting says otherwise.</summary>
    public bool EnableTls { get; set; } = true;

    /// <summary>The user of the server, when it asks for one.</summary>
    public string? User { get; set; }

    /// <summary>The password of that user. A secret.</summary>
    public string? Password { get; set; }

    /// <summary>The address the mails come from.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>The address users open the app at: the links of the mails begin with it.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>True when a server is named.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);

    /// <summary>True when no server is named, or everything a mail needs is there. <paramref name="development"/> admits a link over plain HTTP.</summary>
    public static bool IsUsable(MailOptions options, bool development)
    {
        ArgumentNullException.ThrowIfNull(options);

        return !options.IsConfigured
            || (options.Port is >= 1 and <= 65535
                && MailAddress.TryCreate(options.From, out _)
                && Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var address)
                && (address.Scheme == Uri.UriSchemeHttps || (development && address.Scheme == Uri.UriSchemeHttp)));
    }
}
