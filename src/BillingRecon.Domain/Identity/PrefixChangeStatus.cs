namespace BillingRecon.Domain.Identity;

/// <summary>Where a prefix change stands (ADR 0033).</summary>
public enum PrefixChangeStatus
{
    /// <summary>Asked for, waiting for a checker.</summary>
    Pending = 0,

    /// <summary>Approved: the prefix is the document type's from then on.</summary>
    Approved = 1,

    /// <summary>Rejected: nothing changed.</summary>
    Rejected = 2,
}
