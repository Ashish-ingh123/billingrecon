namespace BillingRecon.Domain.Payments;

/// <summary>Where the money of a refund comes from (BR-307). The member names are stored as text, so renaming one is a data migration.</summary>
public enum RefundSourceKind
{
    /// <summary>What a payment has not applied to any bill: its unapplied cash.</summary>
    Payment = 0,

    /// <summary>What a posted credit note has not applied to any bill: its available credit.</summary>
    CreditNote = 1,
}
