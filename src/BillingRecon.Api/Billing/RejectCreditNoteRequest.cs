using System.Text.Json.Serialization;
using BillingRecon.Application.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Billing;

/// <summary>The body of <c>POST /api/v1/credit-notes/{id}/reject</c>: why the checker rejects (BR-605). Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RejectCreditNoteRequest
{
    /// <summary>The reason. Required; the maker reads it.</summary>
    public string? Comment { get; init; }

    /// <summary>The command for credit note <paramref name="id"/>, at the version the checker read.</summary>
    public RejectCreditNoteCommand ToCommand(CreditNoteId id, string expectedVersion) => new(id, expectedVersion, Comment ?? string.Empty);
}
