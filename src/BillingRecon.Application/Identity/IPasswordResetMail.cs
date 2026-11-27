
namespace BillingRecon.Application.Identity;

/// <summary>
/// The mail of a forgotten password (ADR 0061, amended): whether this deployment can send one, and the sending of the
/// link to a user. The link is made by the implementation from the public address of the app; the use case never
/// sees a mail server.
/// </summary>
public interface IPasswordResetMail
{
    /// <summary>True when a mail server is configured. Without one no link is ever made, and an administrator's reset is the way.</summary>
    public bool IsConfigured { get; }

    /// <summary>
    /// Hands the mail with the link over to be sent, and returns at once: the sending happens after the request, so
    /// how long a request takes says nothing about whether an address is known.
    /// </summary>
    public void Send(string toAddress, string displayName, string tenantCode, string token);
}
