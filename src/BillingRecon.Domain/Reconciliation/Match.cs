using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Reconciliation;

/// <summary>
/// A match of R2 (BR-1006, BR-1007; ADR 0066): statement lines of one channel joined to what they are, payments for money
/// in and refunds for money out, whose amounts add up to the lines' to the paisa. A match posts nothing and moves no
/// money. A line and an item are in at most one active match: each link row carries its own active flag, and a unique
/// index on the active rows refuses a second (BR-1029). A match is never deleted; ending one (Prompt 13.3) keeps it.
/// </summary>
public sealed class Match : AggregateRoot<MatchId>, ITenantScoped, ICreationAudited, IModificationAudited, IHasRowVersion
{
    /// <summary>Longest allowed <see cref="Reason"/>.</summary>
    public const int ReasonMaxLength = 300;

    private readonly List<MatchLine> _lines = [];
    private readonly List<MatchItem> _items = [];

    // Also the constructor Entity Framework uses to load a match; it then sets the other properties.
    private Match(string reason)
    {
        Reason = reason;
        Status = MatchStatus.Active;
        RowVersion = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The channel whose lines the match joins.</summary>
    public SettlementChannelId ChannelId { get; private set; }

    /// <summary>What the lines are: payments or refunds.</summary>
    public MatchItemType Kind { get; private set; }

    /// <summary>How the match was made.</summary>
    public MatchSource Source { get; private set; }

    /// <summary>Why, in words: what agreed between the lines and the items.</summary>
    public string Reason { get; private set; }

    /// <summary>Whether the match stands.</summary>
    public MatchStatus Status { get; private set; }

    /// <summary>The statement lines of the match.</summary>
    public IReadOnlyList<MatchLine> Lines => _lines;

    /// <summary>The payments or refunds of the match.</summary>
    public IReadOnlyList<MatchItem> Items => _items;

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

    /// <summary>
    /// Joins <paramref name="lines"/> of <paramref name="channelId"/> to <paramref name="items"/> of one kind, for
    /// <paramref name="reason"/>. The amounts must add up to the paisa (BR-1006); that no line or item is in another active
    /// match is the use case's question and finally the unique indexes' (BR-1029).
    /// </summary>
    /// <exception cref="ArgumentException">No line or item, an amount not above zero, two of one, or sums that differ.</exception>
    public static Match Make(
        SettlementChannelId channelId,
        MatchItemType kind,
        MatchSource source,
        string reason,
        IReadOnlyCollection<(StatementLineId Line, Money Amount)> lines,
        IReadOnlyCollection<(Guid Item, Money Amount)> items)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(items);

        if (lines.Count == 0 || items.Count == 0)
        {
            throw new ArgumentException("A match joins at least one line to at least one item.", nameof(lines));
        }

        if (lines.Any(line => line.Amount <= Money.Zero) || items.Any(item => item.Amount <= Money.Zero))
        {
            throw new ArgumentException("Every amount of a match is above zero.", nameof(lines));
        }

        if (lines.Select(line => line.Line).Distinct().Count() != lines.Count || items.Select(item => item.Item).Distinct().Count() != items.Count)
        {
            throw new ArgumentException("A line or an item is in a match once.", nameof(items));
        }

        var lineTotal = lines.Aggregate(Money.Zero, (sum, line) => sum + line.Amount);
        var itemTotal = items.Aggregate(Money.Zero, (sum, item) => sum + item.Amount);
        if (lineTotal != itemTotal)
        {
            throw new ArgumentException($"The lines come to {lineTotal.Amount} and the items to {itemTotal.Amount}; a match adds up to the paisa (BR-1006).", nameof(items));
        }

        var match = new Match(Field.Required(reason, ReasonMaxLength))
        {
            ChannelId = channelId,
            Kind = kind,
            Source = source,
        };
        match._lines.AddRange(lines.Select(line => MatchLine.Of(line.Line, line.Amount)));
        match._items.AddRange(items.Select(item => MatchItem.Of(kind, item.Item, item.Amount)));
        match.AddDomainEvent(new MatchMade(channelId, kind, source, match.Reason, lines.Count, items.Count, lineTotal));

        return match;
    }
}
