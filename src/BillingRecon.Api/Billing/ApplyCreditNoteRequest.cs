using System.Text.Json.Serialization;
using BillingRecon.Application.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Billing;

/// <summary>The body of <c>POST /api/v1/credit-notes/{id}/applications</c> (BR-213): the bills and the amount for each. Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApplyCreditNoteRequest
{
    /// <summary>Between 1 and 50 applications; a bill is named once.</summary>
    public IReadOnlyList<CreditApplicationRequest?>? Applications { get; init; }

    /// <summary>
    /// The command for credit note <paramref name="id"/>, at the version the client read. A line left null stays null
    /// and a bill left out becomes the empty id; the validator refuses both.
    /// </summary>
    public ApplyCreditNoteCommand ToCommand(CreditNoteId id, string expectedVersion) => new()
    {
        Id = id,
        ExpectedVersion = expectedVersion,
        Applications = [.. (Applications ?? []).Select(line => line is null ? null! : new CreditApplicationInput(new BillId(line.BillId ?? Guid.Empty), line.Amount))],
    };
}
