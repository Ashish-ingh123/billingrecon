using System.Text.Json.Serialization;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Identity;

/// <summary>The body of <c>POST /api/v1/settings/prefix-changes</c>: the document type, its new prefix and an optional comment. Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequestPrefixChangeRequest
{
    /// <summary><c>Bill</c>, <c>CreditNote</c>, <c>Payment</c>, <c>Adjustment</c> or <c>Refund</c>.</summary>
    public DocumentType? DocumentType { get; init; }

    /// <summary>1 to 3 letters or digits; upper-cased.</summary>
    public string? Prefix { get; init; }

    /// <summary>A word for the checker, at most 500 characters.</summary>
    public string? Comment { get; init; }

    /// <summary>The command.</summary>
    public RequestPrefixChangeCommand ToCommand() => new(DocumentType, Prefix, Comment);
}
