namespace BillingRecon.Domain.Reconciliation;

/// <summary>Which way a statement line's money went (BR-1005). Stored as text.</summary>
public enum StatementDirection
{
    /// <summary>Money reached the account: a credit on the bank's statement.</summary>
    In,

    /// <summary>Money left the account: a debit on the bank's statement.</summary>
    Out,
}
