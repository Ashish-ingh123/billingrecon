namespace BillingRecon.Domain.Common;

/// <summary>
/// Where a bill or a payment comes from (ADR 0053). The member names are stored as text.
/// </summary>
public enum DocumentOrigin
{
    /// <summary>Raised or recorded here, by a user.</summary>
    Standard = 0,

    /// <summary>
    /// An opening balance: brought in from the system before by an approved opening batch, under a number of its own
    /// series, with the number it had there kept beside it.
    /// </summary>
    Opening = 1,
}
