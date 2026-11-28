using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Reconciliation;

/// <summary>
/// A proposal of a matching run (BR-1011; ADR 0066): a statement line and the payments or refunds that may be what it
/// is, by which rule, with a score that orders the proposals of a line and the reason in words. A user accepts or
/// dismisses it (Prompt 13.3); a later run, or a match made otherwise, withdraws it. A run makes and withdraws proposals
/// as bookkeeping and raises no event for them: what is audited is what a user decides, and every match.
/// </summary>
public sealed class MatchProposal : AggregateRoot<MatchProposalId>, ITenantScoped, ICreationAudited, IModificationAudited, IHasRowVersion
{
    /// <summary>Longest allowed <see cref="Reason"/>.</summary>
    public const int ReasonMaxLength = 300;

    private readonly List<MatchProposalItem> _items = [];

    // Also the constructor Entity Framework uses to load a proposal; it then sets the other properties.
    private MatchProposal(string reason)
    {
        Reason = reason;
        Status = MatchProposalStatus.Open;
        RowVersion = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The channel of the line.</summary>
    public SettlementChannelId ChannelId { get; private set; }

    /// <summary>The statement line the proposal is for.</summary>
    public StatementLineId StatementLineId { get; private set; }

    /// <summary>What the items are.</summary>
    public MatchItemType Kind { get; private set; }

    /// <summary>The rule of BR-1011 that found it: 1 the reference, 2 the amount and the date, 3 a combination.</summary>
    public int Rule { get; private set; }

    /// <summary>From 0 to 100: the higher, the likelier. It orders the proposals of a line and decides nothing.</summary>
    public int Score { get; private set; }

    /// <summary>Why, in words.</summary>
    public string Reason { get; private set; }

    /// <summary>Where the proposal stands.</summary>
    public MatchProposalStatus Status { get; private set; }

    /// <summary>The payments or refunds proposed.</summary>
    public IReadOnlyList<MatchProposalItem> Items => _items;

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

    /// <summary>An open proposal of <paramref name="items"/> for <paramref name="line"/>.</summary>
    /// <exception cref="ArgumentException">No item, a rule other than 1 to 3, a score outside 0 to 100.</exception>
    public static MatchProposal Propose(
        SettlementChannelId channelId,
        StatementLineId line,
        MatchItemType kind,
        int rule,
        int score,
        string reason,
        IReadOnlyCollection<(Guid Item, Money Amount)> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0 || rule is < 1 or > 3 || score is < 0 or > 100)
        {
            throw new ArgumentException("A proposal names at least one item, by a rule from 1 to 3, with a score from 0 to 100.", nameof(items));
        }

        var proposal = new MatchProposal(Field.Required(reason, ReasonMaxLength))
        {
            ChannelId = channelId,
            StatementLineId = line,
            Kind = kind,
            Rule = rule,
            Score = score,
        };
        proposal._items.AddRange(items.Select(item => MatchProposalItem.Of(kind, item.Item, item.Amount)));

        return proposal;
    }

    /// <summary>A later run, or a match made otherwise, takes the proposal's place. A proposal no longer open stays as it is.</summary>
    public void Withdraw()
    {
        if (Status == MatchProposalStatus.Open)
        {
            Status = MatchProposalStatus.Withdrawn;
        }
    }
}
