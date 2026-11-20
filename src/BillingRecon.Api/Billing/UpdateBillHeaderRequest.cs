using System.Text.Json.Serialization;
using BillingRecon.Application.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Billing;

/// <summary>
/// The body of <c>PATCH /api/v1/bills/{id}</c>: both dates and the two texts of a draft, and a text left out is
/// cleared. The patient, the payer, the status and the lines cannot be changed here: a body that names them, or any
/// member the API does not know, is refused with 400.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateBillHeaderRequest
{
    /// <summary>The business date of the bill. Required.</summary>
    public DateOnly? BillDate { get; init; }

    /// <summary>The date the bill is due, not before the bill date. Required.</summary>
    public DateOnly? DueDate { get; init; }

    /// <summary>The visit or admission the bill is for.</summary>
    public string? EncounterReference { get; init; }

    /// <summary>A note for whoever reads the bill.</summary>
    public string? Notes { get; init; }

    /// <summary>The command for bill <paramref name="id"/>, at the version the client read.</summary>
    public UpdateBillHeaderCommand ToCommand(BillId id, string expectedVersion) => new()
    {
        Id = id,
        ExpectedVersion = expectedVersion,
        BillDate = BillDate,
        DueDate = DueDate,
        EncounterReference = EncounterReference,
        Notes = Notes,
    };
}
