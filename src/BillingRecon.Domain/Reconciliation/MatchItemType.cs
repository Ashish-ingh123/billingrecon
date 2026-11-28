namespace BillingRecon.Domain.Reconciliation;

/// <summary>What a match joins a statement line to (BR-1006). Stored as text; 13.3 and 13.4 add the rest.</summary>
public enum MatchItemType
{
    /// <summary>A payment of BillingRecon, for money in.</summary>
    Payment,

    /// <summary>A refund of BillingRecon, for money out.</summary>
    Refund,
}
