using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Replaces the dates and the two texts of a draft bill: every field is sent, both dates are required, and a text left
/// out is cleared. The patient, the payer and the lines are not part of it. <see cref="ExpectedVersion"/> is the
/// version the client read (BR-804).
/// </summary>
public sealed record UpdateBillHeaderCommand
{
    /// <summary>The bill to correct.</summary>
    public required BillId Id { get; init; }

    /// <summary>The version the client read, from its If-Match header: the row version in base64.</summary>
    public required string ExpectedVersion { get; init; }

    /// <summary>The business date of the bill; null when the client sent none.</summary>
    public DateOnly? BillDate { get; init; }

    /// <summary>The date the bill is due; null when the client sent none.</summary>
    public DateOnly? DueDate { get; init; }

    /// <summary>The visit or admission the bill is for.</summary>
    public string? EncounterReference { get; init; }

    /// <summary>A note for whoever reads the bill.</summary>
    public string? Notes { get; init; }
}
