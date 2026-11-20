using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Creates a draft bill for a patient, owed by a payer (US-20). A bill date left out is today in India; a due date left
/// out is the bill date plus the tenant's default due days (BR-205). The lines may be empty and can be replaced later.
/// </summary>
public sealed record CreateBillDraftCommand
{
    /// <summary>The patient the bill is for.</summary>
    public required PatientId PatientId { get; init; }

    /// <summary>The payer who owes the bill.</summary>
    public required PayerId PayerId { get; init; }

    /// <summary>The business date of the bill; today in India when null.</summary>
    public DateOnly? BillDate { get; init; }

    /// <summary>The date the bill is due; the bill date plus the tenant's default due days when null.</summary>
    public DateOnly? DueDate { get; init; }

    /// <summary>The visit or admission the bill is for.</summary>
    public string? EncounterReference { get; init; }

    /// <summary>A note for whoever reads the bill.</summary>
    public string? Notes { get; init; }

    /// <summary>The lines, in the order they appear on the bill.</summary>
    public IReadOnlyList<BillLineInput> Lines { get; init; } = [];
}
