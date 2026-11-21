using System.Text.Json.Serialization;
using BillingRecon.Application.Adjustments;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Adjustments;

/// <summary>The body of <c>POST /api/v1/adjustments/{id}/approve</c>: an optional comment of the checker. The body itself may be left out.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApproveAdjustmentRequest
{
    /// <summary>What the checker has to say, at most 500 characters.</summary>
    public string? Comment { get; init; }

    /// <summary>The command for adjustment <paramref name="id"/>, at the version the checker read.</summary>
    public ApproveAdjustmentCommand ToCommand(AdjustmentId id, string expectedVersion) => new(id, expectedVersion, Comment);
}
