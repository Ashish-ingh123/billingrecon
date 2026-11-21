using System.Text.Json.Serialization;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Payments;

/// <summary>The body of <c>POST /api/v1/refunds/{id}/reject</c>: why the checker rejects (BR-605). Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RejectRefundRequest
{
    /// <summary>The reason. Required; the maker reads it.</summary>
    public string? Comment { get; init; }

    /// <summary>The command for refund <paramref name="id"/>, at the version the checker read.</summary>
    public RejectRefundCommand ToCommand(RefundId id, string expectedVersion) => new(id, expectedVersion, Comment ?? string.Empty);
}
