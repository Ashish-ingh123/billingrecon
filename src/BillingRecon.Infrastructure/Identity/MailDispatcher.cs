using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// Sends the mails that wait, one at a time, for as long as the API runs. A mail that cannot be sent is logged as an
/// error without its address or its text and is not tried again: its user asks for a new link.
/// </summary>
public sealed partial class MailDispatcher(MailOutbox queue, IMailTransport transport, ILogger<MailDispatcher> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var mail in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await transport.SendAsync(mail, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Whatever a mail server answers, the next mail is still sent: the loop must outlive one failure.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogNotSent(logger, exception.GetType().Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A mail could not be sent ({Failure}); its user asks for a new one.")]
    private static partial void LogNotSent(ILogger logger, string failure);
}
