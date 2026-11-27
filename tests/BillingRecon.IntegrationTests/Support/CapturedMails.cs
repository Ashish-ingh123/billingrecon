using System.Threading.Channels;
using BillingRecon.Infrastructure.Identity;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// The mail server of a test: keeps what the API would have sent, in the order it was sent. The API sends its mails
/// one at a time after the request, so a test waits for the next one; that nothing was sent for a request shows in
/// the next mail being the one of a later request.
/// </summary>
public sealed class CapturedMails : IMailTransport
{
    private readonly Channel<QueuedMail> _sent = Channel.CreateUnbounded<QueuedMail>();

    /// <inheritdoc />
    public Task SendAsync(QueuedMail mail, CancellationToken ct)
    {
        _sent.Writer.TryWrite(mail);

        return Task.CompletedTask;
    }

    /// <summary>The next mail the API sent; a <see cref="TimeoutException"/> when none comes within ten seconds, or <paramref name="within"/>.</summary>
    public async Task<QueuedMail> NextAsync(TimeSpan? within = null)
    {
        using var patience = new CancellationTokenSource(within ?? TimeSpan.FromSeconds(10));

        try
        {
            return await _sent.Reader.ReadAsync(patience.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("The API sent no mail in time.");
        }
    }
}
