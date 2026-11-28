using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Reconciliation;

/// <summary>
/// A matching run of a channel (BR-1029; ADR 0066): one at a time for a channel, which a unique index on the running rows
/// keeps (two runs at once deadlocked in SQL Server and could only skip each other's lines). A run is finished with its
/// counts; one left running longer than <see cref="StaleAfter"/> was stopped with its process and is abandoned by the next.
/// Operational bookkeeping: it raises no event, and what a run made, its matches, is audited.
/// </summary>
public sealed class MatchingRun : AggregateRoot<MatchingRunId>, ITenantScoped, ICreationAudited, IModificationAudited, IHasRowVersion
{
    /// <summary>How long a run may be under way before a new run takes it for stopped.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    // Also the constructor Entity Framework uses to load a run; it then sets the other properties.
    private MatchingRun()
    {
        Status = MatchingRunStatus.Running;
        RowVersion = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The channel the run matches.</summary>
    public SettlementChannelId ChannelId { get; private set; }

    /// <summary>Where the run stands.</summary>
    public MatchingRunStatus Status { get; private set; }

    /// <summary>The unmatched lines it read.</summary>
    public int LinesRead { get; private set; }

    /// <summary>The lines it matched by itself.</summary>
    public int Matched { get; private set; }

    /// <summary>The lines it proposed matches for.</summary>
    public int Proposed { get; private set; }

    /// <summary>The lines it skipped, which a user changed first.</summary>
    public int Skipped { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? CreatedByUserId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? UpdatedByUserId { get; private set; }

    /// <inheritdoc />
    public byte[] RowVersion { get; private set; }

    /// <summary>A run of <paramref name="channelId"/>, under way.</summary>
    public static MatchingRun Start(SettlementChannelId channelId) => new() { ChannelId = channelId };

    /// <summary>Whether a run still marked as under way at <paramref name="nowUtc"/> was in fact stopped.</summary>
    public bool IsStale(DateTimeOffset nowUtc) => Status == MatchingRunStatus.Running && nowUtc - CreatedAtUtc > StaleAfter;

    /// <summary>A stopped run, which a new one takes the place of.</summary>
    public void Abandon()
    {
        if (Status == MatchingRunStatus.Running)
        {
            Status = MatchingRunStatus.Abandoned;
        }
    }

    /// <summary>The run is done, with its counts.</summary>
    public void Finish(int linesRead, int matched, int proposed, int skipped)
    {
        if (Status != MatchingRunStatus.Running)
        {
            throw new InvalidOperationException($"Only a run under way is finished; this one is {Status}.");
        }

        (Status, LinesRead, Matched, Proposed, Skipped) = (MatchingRunStatus.Finished, linesRead, matched, proposed, skipped);
    }
}
