namespace BillingRecon.Domain.Reconciliation;

/// <summary>
/// Where a statement line stands (BR-1004): the one thing about a line that changes, following its matches (Prompts
/// 13.2 and 13.3). Stored as text.
/// </summary>
public enum StatementLineStatus
{
    /// <summary>Nothing explains the line yet.</summary>
    Unmatched,

    /// <summary>An active match joins the line to what it is.</summary>
    Matched,

    /// <summary>The line is not a patient's money, and says why (BR-1012).</summary>
    Excluded,
}
