using System.Text.Json.Serialization;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Payments;

/// <summary>The body of <c>POST /api/v1/refunds/{id}/void</c>: why the draft is given up. Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VoidRefundRequest
{
    /// <summary>The reason, at most 500 characters. Required.</summary>
    public string? Reason { get; init; }

    /// <summary>The command for refund <paramref name="id"/>, at the version the client read.</summary>
    public VoidRefundCommand ToCommand(RefundId id, string expectedVersion) => new(id, expectedVersion, Reason ?? string.Empty);
}
