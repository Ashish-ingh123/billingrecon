namespace BillingRecon.Domain.Common;

/// <summary>
/// The kinds of document that are numbered (BR-801): each has its own gapless series per tenant and fiscal year, and
/// its own default prefix among the tenant's settings (BR-802). The member names are stored as text
/// (docs/database.md, section 3.6), so renaming one is a data migration.
/// </summary>
public enum DocumentType
{
    /// <summary>A bill; default prefix <c>BIL</c>.</summary>
    Bill,

    /// <summary>A credit note; default prefix <c>CN</c>.</summary>
    CreditNote,

    /// <summary>A payment; default prefix <c>PAY</c>.</summary>
    Payment,

    /// <summary>An adjustment; default prefix <c>ADJ</c>.</summary>
    Adjustment,

    /// <summary>A refund; default prefix <c>REF</c>.</summary>
    Refund,
}
