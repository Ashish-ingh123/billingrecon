namespace BillingRecon.Domain.Reconciliation;

/// <summary>How a bank's CSV writes the money of a line (BR-1002). Stored as text inside the column mapping.</summary>
public enum AmountLayout
{
    /// <summary>
    /// One column with a signed amount: above zero is money in, below zero money out. A trailing "Cr" or "Dr" (any case)
    /// gives the direction instead of the sign, as some banks write it.
    /// </summary>
    OneColumn,

    /// <summary>Two columns, the debit (money out) and the credit (money in); exactly one holds an amount on each row.</summary>
    TwoColumns,
}
