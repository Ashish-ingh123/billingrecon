using System.Text.Json.Serialization;
using BillingRecon.Application.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Billing;

/// <summary>The body of <c>PUT /api/v1/credit-notes/{id}/lines</c>: every line the draft is to have, in order. Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReplaceCreditNoteLinesRequest
{
    /// <summary>The lines, in the order they appear on the credit note.</summary>
    public IReadOnlyList<CreditNoteLineRequest?>? Lines { get; init; }

    /// <summary>The command for credit note <paramref name="id"/>, at the version the client read.</summary>
    public ReplaceCreditNoteLinesCommand ToCommand(CreditNoteId id, string expectedVersion) => new()
    {
        Id = id,
        ExpectedVersion = expectedVersion,
        Lines = Lines.ToInputs(),
    };
}
