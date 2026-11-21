using System.Text.Json.Serialization;
using BillingRecon.Application.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Billing;

/// <summary>The body of <c>PATCH /api/v1/credit-notes/{id}</c>: the date and the reason of a draft. Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateCreditNoteHeaderRequest
{
    /// <summary>The business date of the credit note. Required.</summary>
    public DateOnly? CreditNoteDate { get; init; }

    /// <summary>Why the credit is given. Required.</summary>
    public string? Reason { get; init; }

    /// <summary>The command for credit note <paramref name="id"/>, at the version the client read.</summary>
    public UpdateCreditNoteHeaderCommand ToCommand(CreditNoteId id, string expectedVersion) => new(id, expectedVersion, CreditNoteDate, Reason ?? string.Empty);
}
