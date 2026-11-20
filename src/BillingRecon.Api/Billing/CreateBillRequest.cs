using System.Text.Json.Serialization;
using BillingRecon.Application.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Billing;

/// <summary>
/// The body of <c>POST /api/v1/bills</c>: a draft bill. A member the API does not know is refused with 400, so a
/// misspelt field cannot pass unnoticed.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateBillRequest
{
    /// <summary>The patient the bill is for. Cannot be changed later.</summary>
    public Guid? PatientId { get; init; }

    /// <summary>The payer who owes the bill. Cannot be changed later.</summary>
    public Guid? PayerId { get; init; }

    /// <summary>The business date of the bill; today in India when left out (BR-205).</summary>
    public DateOnly? BillDate { get; init; }

    /// <summary>The date the bill is due; the bill date plus the hospital's default due days when left out (BR-205).</summary>
    public DateOnly? DueDate { get; init; }

    /// <summary>The visit or admission the bill is for.</summary>
    public string? EncounterReference { get; init; }

    /// <summary>A note for whoever reads the bill.</summary>
    public string? Notes { get; init; }

    /// <summary>The lines, in the order they appear on the bill; may be left out and added later.</summary>
    public IReadOnlyList<BillLineRequest?>? Lines { get; init; }

    /// <summary>The command; a missing id becomes the empty id, which the validator reports.</summary>
    public CreateBillDraftCommand ToCommand() => new()
    {
        PatientId = new PatientId(PatientId ?? Guid.Empty),
        PayerId = new PayerId(PayerId ?? Guid.Empty),
        BillDate = BillDate,
        DueDate = DueDate,
        EncounterReference = EncounterReference,
        Notes = Notes,
        Lines = Lines.ToInputs(),
    };
}
