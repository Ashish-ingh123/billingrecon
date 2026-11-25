using System.Collections.Concurrent;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// The sessions of the users where no database is there to read them (ADR 0057): every token is of its user's current
/// session, until a test ends the sessions of a user. The hosts on a real database (<c>DatabaseApiFactory</c>) read the
/// user's row instead.
/// </summary>
public sealed class CurrentSessions : IUserSessionReader
{
    private readonly ConcurrentDictionary<UserId, bool> _ended = new();

    /// <summary>What the reader was asked, in order: the user and the number of the session of each token.</summary>
    public ConcurrentQueue<(UserId UserId, int SessionVersion)> Asked { get; } = new();

    /// <summary>From now on no token of <paramref name="userId"/> is current.</summary>
    public void End(UserId userId) => _ended[userId] = true;

    /// <inheritdoc />
    public Task<bool> IsCurrentAsync(UserId userId, int sessionVersion, CancellationToken ct)
    {
        Asked.Enqueue((userId, sessionVersion));

        return Task.FromResult(!_ended.ContainsKey(userId));
    }
}
