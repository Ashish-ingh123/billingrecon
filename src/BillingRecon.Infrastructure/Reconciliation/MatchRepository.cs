using BillingRecon.Application.Reconciliation;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Reconciliation;

/// <summary>What a matching run reads and writes, through the request's context: filtered by its tenant, saved by its unit of work.</summary>
public sealed class MatchRepository(BillingReconDbContext context) : IMatchRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<StatementLine>> UnmatchedLinesAsync(SettlementChannelId channelId, CancellationToken ct)
        => await context.Set<StatementLine>().AsNoTracking()
            .Where(line => line.ChannelId == channelId && line.Status == StatementLineStatus.Unmatched)
            .OrderBy(line => line.TransactionDate)
            .ThenBy(line => line.RowNumber)
            .ThenBy(line => line.Id)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlySet<Guid>> ActiveItemIdsAsync(MatchItemType type, CancellationToken ct)
        => (await context.Set<MatchItem>().AsNoTracking()
            .Where(item => item.ItemType == type && item.IsActive)
            .Select(item => item.ItemId)
            .ToListAsync(ct)).ToHashSet();

    /// <inheritdoc />
    public async Task<IReadOnlyList<StatementLine>> UnmatchedLinesAsync(IReadOnlyCollection<StatementLineId> ids, CancellationToken ct)
        => await context.Set<StatementLine>().Where(line => ids.Contains(line.Id) && line.Status == StatementLineStatus.Unmatched).ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<MatchProposal>> OpenProposalsAsync(IReadOnlyCollection<StatementLineId> ids, CancellationToken ct)
        => await context.Set<MatchProposal>().Where(proposal => ids.Contains(proposal.StatementLineId) && proposal.Status == MatchProposalStatus.Open).ToListAsync(ct);

    /// <inheritdoc />
    public Task<MatchingRun?> RunningRunAsync(SettlementChannelId channelId, CancellationToken ct)
        => context.Set<MatchingRun>().SingleOrDefaultAsync(run => run.ChannelId == channelId && run.Status == MatchingRunStatus.Running, ct);

    /// <inheritdoc />
    public Task<MatchingRun?> RunAsync(MatchingRunId id, CancellationToken ct)
        => context.Set<MatchingRun>().SingleOrDefaultAsync(run => run.Id == id, ct);

    /// <inheritdoc />
    public void Add(MatchingRun run) => context.Set<MatchingRun>().Add(run);

    /// <inheritdoc />
    public void Add(Match match) => context.Set<Match>().Add(match);

    /// <inheritdoc />
    public void Add(MatchProposal proposal) => context.Set<MatchProposal>().Add(proposal);

    /// <inheritdoc />
    public void Forget() => context.ChangeTracker.Clear();
}
