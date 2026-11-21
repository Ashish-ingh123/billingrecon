using System.Text.Json.Serialization;
using BillingRecon.Application.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Billing;

/// <summary>The body of <c>POST /api/v1/credit-notes/{id}/void</c>: why the draft is voided. Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VoidCreditNoteRequest
{
    /// <summary>The reason. Required; it is kept with the credit note.</summary>
    public string? Reason { get; init; }

    /// <summary>The command for credit note <paramref name="id"/>, at the version the client read.</summary>
    public VoidCreditNoteCommand ToCommand(CreditNoteId id, string expectedVersion) => new(id, expectedVersion, Reason ?? string.Empty);
}
