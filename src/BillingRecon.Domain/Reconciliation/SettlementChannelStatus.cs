namespace BillingRecon.Domain.Reconciliation;

/// <summary>Whether a settlement channel takes new imports (BR-1001). Stored as text.</summary>
public enum SettlementChannelStatus
{
    /// <summary>The channel takes imports.</summary>
    Active,

    /// <summary>The channel takes no import; what was imported into it stays.</summary>
    Inactive,
}
