
namespace BillingRecon.Infrastructure.Identity;

/// <summary>How one mail is sent. The SMTP client in production; a test puts its own in its place.</summary>
public interface IMailTransport
{
    /// <summary>Sends <paramref name="mail"/>, or throws.</summary>
    public Task SendAsync(QueuedMail mail, CancellationToken ct);
}
