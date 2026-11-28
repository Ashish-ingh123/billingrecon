using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Reconciliation;

/// <summary>
/// A payment or refund inside a match, named by its kind and its id (Reconciliation names no entity of Payments, ADR
/// 0065), with the amount the match takes. <see cref="IsActive"/> keeps an item in one active match, as on
/// <see cref="MatchLine"/> (BR-1029).
/// </summary>
public sealed class MatchItem : Entity<MatchItemId>, ITenantScoped
{
    // Entity Framework creates an item with this constructor and then sets the properties.
    private MatchItem()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The match. Set when the match is saved.</summary>
    public MatchId MatchId { get; private set; }

    /// <summary>A payment or a refund.</summary>
    public MatchItemType ItemType { get; private set; }

    /// <summary>The id of the payment or refund.</summary>
    public Guid ItemId { get; private set; }

    /// <summary>The amount of the item the match takes.</summary>
    public Money Amount { get; private set; }

    /// <summary>Whether the match stands; false once it is ended.</summary>
    public bool IsActive { get; private set; }

    internal static MatchItem Of(MatchItemType type, Guid item, Money amount) => new() { ItemType = type, ItemId = item, Amount = amount, IsActive = true };
}
