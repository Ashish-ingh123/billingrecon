using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// Sends a mail with the SMTP client of the BCL (ADR 0061, amended; the owner's decision of 2026-11-27 for no
/// package). Microsoft recommends another library for new code; the client is kept behind <see cref="IMailTransport"/>
/// so that it can be replaced without touching a use case.
/// </summary>
public sealed class SmtpMailTransport(IOptions<MailOptions> options) : IMailTransport
{
    /// <inheritdoc />
    public async Task SendAsync(QueuedMail mail, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mail);

        var settings = options.Value;
        using var client = new SmtpClient(settings.Host, settings.Port) { EnableSsl = settings.EnableTls };
        if (!string.IsNullOrEmpty(settings.User))
        {
            client.Credentials = new NetworkCredential(settings.User, settings.Password);
        }

        using var message = new MailMessage(settings.From, mail.To, mail.Subject, mail.Body) { IsBodyHtml = false };
        await client.SendMailAsync(message, ct);
    }
}
