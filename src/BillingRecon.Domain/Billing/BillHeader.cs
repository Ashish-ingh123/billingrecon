namespace BillingRecon.Domain.Billing;

/// <summary>
/// The part of a draft bill a clerk enters and corrects besides its lines: the two business dates (BR-205) and two
/// free texts. The patient and the payer are fixed when the draft is created. An optional text may be null or blank;
/// <see cref="Bill"/> stores text without surrounding white space, and a blank one as null.
/// </summary>
public sealed record BillHeader
{
    /// <summary>The business date of the bill, a calendar date in India (BR-003).</summary>
    public required DateOnly BillDate { get; init; }

    /// <summary>The date the bill is due, not before <see cref="BillDate"/>.</summary>
    public required DateOnly DueDate { get; init; }

    /// <summary>The visit or admission the bill is for, as the hospital's own system names it. Free text.</summary>
    public string? EncounterReference { get; init; }

    /// <summary>A note for whoever reads the bill.</summary>
    public string? Notes { get; init; }
}
