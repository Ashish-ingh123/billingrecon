using System.Text.Json.Serialization;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Payments;

/// <summary>The body of <c>POST /api/v1/refunds/{id}/approve</c>: an optional comment of the checker. The body itself may be left out.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApproveRefundRequest
{
    /// <summary>What the checker has to say, at most 500 characters.</summary>
    public string? Comment { get; init; }

    /// <summary>The command for refund <paramref name="id"/>, at the version the checker read.</summary>
    public ApproveRefundCommand ToCommand(RefundId id, string expectedVersion) => new(id, expectedVersion, Comment);
}
