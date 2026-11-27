using System.Threading.Channels;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// The mails that wait to be sent, in the memory of this instance (ADR 0061, amended). A request hands a mail over and
/// answers; <see cref="MailDispatcher"/> sends it afterwards. At most 200 wait: beyond that a new one is dropped, and
/// its user asks again. A restart forgets what waited, for the same reason acceptable: a link is asked for again.
/// </summary>
public sealed class MailOutbox
{
    private readonly Channel<QueuedMail> _channel = Channel.CreateBounded<QueuedMail>(new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropWrite });

    /// <summary>The mails, for the one reader.</summary>
    public ChannelReader<QueuedMail> Reader => _channel.Reader;

    /// <summary>Hands a mail over. When 200 wait already it is dropped, and the answer does not say so.</summary>
    public bool TryAdd(QueuedMail mail) => _channel.Writer.TryWrite(mail);
}
