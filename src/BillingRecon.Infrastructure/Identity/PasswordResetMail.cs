using BillingRecon.Application.Identity;
using Microsoft.Extensions.Options;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// The mail of a forgotten password (ADR 0061, amended): the link, in plain text, to the address the hospital keeps
/// for the user. The link carries its token and the hospital's code in the fragment of the address, which a browser
/// never sends to a server, so neither reaches an access log.
/// </summary>
public sealed class PasswordResetMail(IOptions<MailOptions> options, MailOutbox queue) : IPasswordResetMail
{
    /// <inheritdoc />
    public bool IsConfigured => options.Value.IsConfigured;

    /// <inheritdoc />
    public void Send(string toAddress, string displayName, string tenantCode, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var link = $"{options.Value.PublicBaseUrl.TrimEnd('/')}/reset-password#token={Uri.EscapeDataString(token)}&hospital={Uri.EscapeDataString(tenantCode)}";
        var body = string.Join(
            "\r\n",
            $"Hello {displayName},",
            string.Empty,
            "A new password was asked for your BillingRecon account. Open this link to choose one:",
            string.Empty,
            link,
            string.Empty,
            "The link works once and for 30 minutes. If you did not ask for it, do nothing: your password stays as it is.",
            string.Empty,
            "BillingRecon");

        queue.TryAdd(new QueuedMail(toAddress, "Choose a new BillingRecon password", body));
    }
}
