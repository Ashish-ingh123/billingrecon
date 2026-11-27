using BillingRecon.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BillingRecon.Infrastructure.Common.Operations;

/// <summary>
/// The alerts of ADR 0056: an error in the log, and a plain-text mail to <c>Alerts:MailTo</c> through the queue of the
/// mail server (<see cref="MailOutbox"/>) when both are set.
/// </summary>
public sealed partial class OperationsAlerts(
    IOptions<AlertOptions> alerts,
    IOptions<MailOptions> mail,
    MailOutbox outbox,
    ILogger<OperationsAlerts> logger) : IOperationsAlerts
{
    /// <summary>The prefix of every alert's subject, so a mailbox can sort them.</summary>
    public const string SubjectPrefix = "[BillingRecon alert] ";

    /// <inheritdoc />
    public bool Delivers => alerts.Value.IsConfigured && mail.Value.IsConfigured;

    /// <inheritdoc />
    public void Raise(string subject, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentNullException.ThrowIfNull(body);

        LogAlert(logger, subject, body);
        if (Delivers)
        {
            outbox.TryAdd(new QueuedMail(alerts.Value.MailTo.Trim(), SubjectPrefix + subject, body));
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Operations alert: {AlertSubject}. {AlertBody}")]
    private static partial void LogAlert(ILogger logger, string alertSubject, string alertBody);
}
