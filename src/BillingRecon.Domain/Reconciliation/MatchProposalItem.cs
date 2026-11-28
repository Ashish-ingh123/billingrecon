using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Reconciliation;

/// <summary>A payment or refund a proposal names (BR-1011), by its kind and id, with its amount.</summary>
public sealed class MatchProposalItem : Entity<MatchProposalItemId>, ITenantScoped
{
    // Entity Framework creates an item with this constructor and then sets the properties.
    private MatchProposalItem()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The proposal. Set when the proposal is saved.</summary>
    public MatchProposalId ProposalId { get; private set; }

    /// <summary>A payment or a refund.</summary>
    public MatchItemType ItemType { get; private set; }

    /// <summary>The id of the payment or refund.</summary>
    public Guid ItemId { get; private set; }

    /// <summary>Its amount.</summary>
    public Money Amount { get; private set; }

    internal static MatchProposalItem Of(MatchItemType type, Guid item, Money amount) => new() { ItemType = type, ItemId = item, Amount = amount };
}
