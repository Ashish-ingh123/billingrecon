namespace BillingRecon.Domain.Identity;

/// <summary>Which image of the letterhead (ADR 0059).</summary>
public enum LetterheadImageKind
{
    /// <summary>The hospital's logo, at the head of a printed document.</summary>
    Logo = 1,

    /// <summary>The signatory's signature, at its foot.</summary>
    Signature = 2,
}
