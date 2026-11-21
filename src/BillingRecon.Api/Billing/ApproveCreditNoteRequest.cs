using System.Text.Json.Serialization;
using BillingRecon.Application.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Billing;

/// <summary>The body of <c>POST /api/v1/credit-notes/{id}/approve</c>: an optional comment of the checker. The body itself may be left out.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApproveCreditNoteRequest
{
    /// <summary>What the checker has to say, at most 500 characters.</summary>
    public string? Comment { get; init; }

    /// <summary>The command for credit note <paramref name="id"/>, at the version the checker read.</summary>
    public ApproveCreditNoteCommand ToCommand(CreditNoteId id, string expectedVersion) => new(id, expectedVersion, Comment);
}
