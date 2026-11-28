namespace BillingRecon.Domain.Reconciliation;

/// <summary>Whether a match stands (BR-1007). Stored as text.</summary>
public enum MatchStatus
{
    /// <summary>The match stands: its lines and items are in no other active match.</summary>
    Active,

    /// <summary>The match was ended; it is kept, and its lines and items are free again.</summary>
    Ended,
}
