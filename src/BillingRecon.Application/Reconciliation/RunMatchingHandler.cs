using BillingRecon.Application.Common;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// A matching run over a bank channel (BR-1008 to BR-1011, BR-1029; ADR 0066). It reads the channel's unmatched lines and
/// the payments (for money in) and refunds (for money out) that no active match holds, through Payments' read port, and
/// lets <see cref="StatementMatcher"/> decide. Then, a batch of lines at a time, each batch in a transaction of its own: a
/// line's open proposals are withdrawn; a certain match is made, the line marked matched; or new proposals are stored. A
/// batch in which another run or a user changed a line or an item first fails its commit on a unique index or a row
/// version; it is dropped and its lines are done again one by one, each in a transaction of its own, and a line that is no
/// longer unmatched is counted as skipped. (One transaction for each line took over 100 seconds for 10,000 lines.) A run posts nothing and moves no money.
/// </summary>
public sealed class RunMatchingHandler(
    ISettlementChannelRepository channels,
    ISettlementChannelReadService channelReads,
    IMatchRepository matches,
    IMatchablePayments payments,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<RunMatchingCommand, MatchingRunDto>
{
    /// <summary>
    /// How many lines one transaction carries. The unit of work's work on a commit grows with the square of what it holds
    /// (each changed child looks for its root among the tracked rows): 500 lines took 4.8 s a batch and 100 about 0.25 s.
    /// </summary>
    public const int BatchSize = 100;

    /// <inheritdoc />
    public async Task<Result<MatchingRunDto>> HandleAsync(RunMatchingCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var channel = await channels.GetAsync(command.ChannelId, ct);
        if (channel is null)
        {
            return ReconciliationErrors.SettlementChannelNotFound();
        }

        if (channel.Kind != SettlementChannelKind.Bank)
        {
            return ReconciliationErrors.SettlementChannelNotBank();
        }

        // One run of a channel at a time (BR-1029): a run under way refuses a second, and a run left under way by a
        // stopped process is abandoned. The unique index on the running rows decides two that start at once.
        var running = await matches.RunningRunAsync(channel.Id, ct);
        if (running is not null)
        {
            if (!running.IsStale(clock.UtcNow))
            {
                return ReconciliationErrors.MatchingRunInProgress();
            }

            running.Abandon();
        }

        var run = MatchingRun.Start(channel.Id);
        matches.Add(run);
        var started = await unitOfWork.CommitAsync(ct);
        if (started.IsFailure)
        {
            return LostARace(started.Error) ? ReconciliationErrors.MatchingRunInProgress() : started.Error;
        }

        var runId = run.Id;
        var result = await MatchAsync(channel, ct);

        // The run is finished with its counts, or abandoned when it failed, so that it never holds the channel.
        matches.Forget();
        var done = await matches.RunAsync(runId, ct);
        if (done is not null)
        {
            if (result.IsSuccess)
            {
                done.Finish(result.Value.LinesRead, result.Value.Matched, result.Value.Proposed, result.Value.Skipped);
            }
            else
            {
                done.Abandon();
            }

            var finished = await unitOfWork.CommitAsync(ct);
            if (finished.IsFailure && result.IsSuccess)
            {
                return finished.Error;
            }
        }

        return result;
    }

    private async Task<Result<MatchingRunDto>> MatchAsync(SettlementChannel channel, CancellationToken ct)
    {
        var lines = await matches.UnmatchedLinesAsync(channel.Id, ct);
        if (lines.Count == 0)
        {
            return new MatchingRunDto(0, 0, 0, 0, 0);
        }

        var from = lines.Min(line => line.TransactionDate).AddDays(-Math.Max(channel.DateWindowDays, StatementMatcher.ReferenceLookBackDays));
        var to = lines.Max(line => line.TransactionDate);
        var withUpiAndCard = !await channelReads.HasGatewayIntoAsync(channel.Id, ct);

        var decisions = new Dictionary<StatementLineId, (MatchItemType Kind, MatchDecision Decision)>();
        foreach (var (direction, kind) in new[] { (StatementDirection.In, MatchItemType.Payment), (StatementDirection.Out, MatchItemType.Refund) })
        {
            var sideLines = lines.Where(line => line.Direction == direction).ToList();
            if (sideLines.Count == 0)
            {
                continue;
            }

            var documents = kind == MatchItemType.Payment
                ? await payments.PaymentsAsync(from, to, withUpiAndCard, ct)
                : await payments.RefundsAsync(from, to, withUpiAndCard, ct);
            var taken = await matches.ActiveItemIdsAsync(kind, ct);
            var candidates = documents
                .Where(document => !taken.Contains(document.Id))
                .Select(document => new MatchCandidate(document.Id, document.Number, document.PayerId, document.Date, document.Method, document.Reference, Money.Of(document.Amount)))
                .ToList();
            var toMatch = sideLines.Select(line => new LineToMatch(line.Id, line.TransactionDate, line.Amount, line.Reference, line.Description)).ToList();

            foreach (var (lineId, decision) in StatementMatcher.Decide(toMatch, candidates, channel.DateWindowDays))
            {
                decisions[lineId] = (kind, decision);
            }
        }

        // The context is cleared after every batch: what a commit audits and checks grows with what it tracks, and a batch
        // of 500 committed with all 10,000 lines and every earlier match tracked took 36 seconds.
        matches.Forget();
        var tally = new Tally();
        foreach (var batch in lines.Chunk(BatchSize))
        {
            var ids = batch.Select(line => line.Id).ToList();
            var tracked = (await matches.UnmatchedLinesAsync(ids, ct)).ToDictionary(line => line.Id);
            var open = (await matches.OpenProposalsAsync(ids, ct)).ToLookup(proposal => proposal.StatementLineId);
            var outcomes = new List<Outcome>();
            foreach (var id in ids)
            {
                if (tracked.TryGetValue(id, out var line))
                {
                    outcomes.Add(Apply(channel.Id, line, decisions[id], open[id]));
                }
                else
                {
                    tally.Skipped++;
                }
            }

            var committed = await unitOfWork.CommitAsync(ct);
            matches.Forget();
            if (committed.IsSuccess)
            {
                tally.Add(outcomes);
                continue;
            }

            if (!LostARace(committed.Error))
            {
                return committed.Error;
            }

            // Another run or a user took a line or an item of the batch first (BR-1029): the batch is dropped, and each of
            // its lines read again and redone in a transaction of its own; a line that is no longer unmatched is skipped.
            foreach (var id in tracked.Keys)
            {
                var again = (await matches.UnmatchedLinesAsync([id], ct)).SingleOrDefault();
                if (again is null)
                {
                    tally.Skipped++;
                    continue;
                }

                var outcome = Apply(channel.Id, again, decisions[id], await matches.OpenProposalsAsync([id], ct));
                var single = await unitOfWork.CommitAsync(ct);
                matches.Forget();
                if (single.IsSuccess)
                {
                    tally.Add([outcome]);
                }
                else if (LostARace(single.Error))
                {
                    tally.Skipped++;
                }
                else
                {
                    return single.Error;
                }
            }
        }

        return new MatchingRunDto(lines.Count, tally.Matched, tally.Proposed, tally.Left, tally.Skipped);
    }

    // What the run does with one line, in the unit of work: its open proposals withdrawn, then a match or new proposals.
    private Outcome Apply(SettlementChannelId channelId, StatementLine line, (MatchItemType Kind, MatchDecision Decision) decided, IEnumerable<MatchProposal> stale)
    {
        var (kind, decision) = decided;
        foreach (var proposal in stale)
        {
            proposal.Withdraw();
        }

        if (decision.Confirmed is { } items)
        {
            line.MarkMatched();
            matches.Add(Match.Make(channelId, kind, MatchSource.Automatic, decision.Reason!, [(line.Id, line.Amount)], [.. items.Select(item => (item.Id, item.Amount))]));
            return Outcome.Matched;
        }

        foreach (var proposal in decision.Proposals)
        {
            matches.Add(MatchProposal.Propose(channelId, line.Id, kind, proposal.Rule, proposal.Score, proposal.Reason, [.. proposal.Items.Select(item => (item.Id, item.Amount))]));
        }

        return decision.Proposals.Count > 0 ? Outcome.Proposed : Outcome.Left;
    }

    private static bool LostARace(Error error) => error.Code is CommonErrors.DuplicateKeyCode or CommonErrors.ConcurrencyConflictCode;

    private enum Outcome
    {
        Matched,
        Proposed,
        Left,
    }

    private sealed class Tally
    {
        public int Matched { get; private set; }

        public int Proposed { get; private set; }

        public int Left { get; private set; }

        public int Skipped { get; set; }

        public void Add(IEnumerable<Outcome> outcomes)
        {
            foreach (var outcome in outcomes)
            {
                switch (outcome)
                {
                    case Outcome.Matched:
                        Matched++;
                        break;
                    case Outcome.Proposed:
                        Proposed++;
                        break;
                    default:
                        Left++;
                        break;
                }
            }
        }
    }
}
