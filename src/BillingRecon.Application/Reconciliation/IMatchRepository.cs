using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// What a matching run reads and writes (ADR 0066): the unmatched lines of a channel and its open proposals, tracked for a
/// change; the items an active match holds; new matches and proposals. Filtered by the tenant. A run commits a batch
/// of lines at a time and clears the context with <see cref="Forget"/> after each.
/// </summary>
public interface IMatchRepository
{
    /// <summary>The channel's unmatched lines, untracked, by date and row: what a run decides from.</summary>
    public Task<IReadOnlyList<StatementLine>> UnmatchedLinesAsync(SettlementChannelId channelId, CancellationToken ct);

    /// <summary>The ids of the items of <paramref name="type"/> that an active match holds.</summary>
    public Task<IReadOnlySet<Guid>> ActiveItemIdsAsync(MatchItemType type, CancellationToken ct);

    /// <summary>Of <paramref name="ids"/>, the lines still unmatched, tracked: read again after a batch lost a race.</summary>
    public Task<IReadOnlyList<StatementLine>> UnmatchedLinesAsync(IReadOnlyCollection<StatementLineId> ids, CancellationToken ct);

    /// <summary>The open proposals of <paramref name="ids"/>, tracked.</summary>
    public Task<IReadOnlyList<MatchProposal>> OpenProposalsAsync(IReadOnlyCollection<StatementLineId> ids, CancellationToken ct);

    /// <summary>The run of the channel marked as under way, tracked, or null.</summary>
    public Task<MatchingRun?> RunningRunAsync(SettlementChannelId channelId, CancellationToken ct);

    /// <summary>A run by its id, tracked, or null.</summary>
    public Task<MatchingRun?> RunAsync(MatchingRunId id, CancellationToken ct);

    /// <summary>Adds a new run to the unit of work.</summary>
    public void Add(MatchingRun run);

    /// <summary>Adds a new match to the unit of work.</summary>
    public void Add(Match match);

    /// <summary>Adds a new proposal to the unit of work.</summary>
    public void Add(MatchProposal proposal);

    /// <summary>
    /// Forgets everything the context tracks, saved or not: after each batch of a run, so that the next commit has only its
    /// own batch to audit and check, and after a failed commit, so that the next does not carry what it left.
    /// </summary>
    public void Forget();
}
