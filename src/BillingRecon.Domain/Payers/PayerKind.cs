namespace BillingRecon.Domain.Payers;

/// <summary>
/// Who a payer is (BR-103). Only <see cref="SelfPay"/> exists in R1; the other two members are reserved for R4 so that
/// the API can name them, and creating such a payer is refused. The member names are stored as text, so renaming one is
/// a data migration.
/// </summary>
public enum PayerKind
{
    /// <summary>The patient pays, or a guarantor pays for them.</summary>
    SelfPay,

    /// <summary>An insurer or a third-party administrator. Reserved for R4.</summary>
    Insurer,

    /// <summary>A corporate account. Reserved for R4.</summary>
    Corporate,
}
