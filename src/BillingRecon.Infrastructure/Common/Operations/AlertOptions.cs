using System.Net.Mail;

namespace BillingRecon.Infrastructure.Common.Operations;

/// <summary>
/// Where the operator's alerts go (ADR 0056): <c>Alerts:MailTo</c>, one address, sent through the mail server of a
/// forgotten password (<c>Mail:*</c>, ADR 0061 amended). Empty: alerts are written to the log only, and the API says so
/// at startup.
/// </summary>
public sealed class AlertOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Alerts";

    /// <summary>The message of an address that is none.</summary>
    public const string InvalidMessage = "Alerts:MailTo is an e-mail address, or empty for alerts in the log only.";

    /// <summary>The operator's address; empty for none.</summary>
    public string MailTo { get; set; } = string.Empty;

    /// <summary>True when an address is named.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(MailTo);

    /// <summary>True when no address is named, or the address is one.</summary>
    public static bool IsUsable(AlertOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return !options.IsConfigured || MailAddress.TryCreate(options.MailTo.Trim(), out _);
    }
}
