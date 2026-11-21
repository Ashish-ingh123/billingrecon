using System.Text.Json.Serialization;
using BillingRecon.Application.Adjustments;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Adjustments;

/// <summary>The body of <c>POST /api/v1/adjustments/{id}/reject</c>: why the checker rejects (BR-605). Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RejectAdjustmentRequest
{
    /// <summary>The reason. Required; the maker reads it.</summary>
    public string? Comment { get; init; }

    /// <summary>The command for adjustment <paramref name="id"/>, at the version the checker read.</summary>
    public RejectAdjustmentCommand ToCommand(AdjustmentId id, string expectedVersion) => new(id, expectedVersion, Comment ?? string.Empty);
}
