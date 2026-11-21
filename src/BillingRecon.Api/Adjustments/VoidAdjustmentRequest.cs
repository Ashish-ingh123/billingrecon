using System.Text.Json.Serialization;
using BillingRecon.Application.Adjustments;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Adjustments;

/// <summary>The body of <c>POST /api/v1/adjustments/{id}/void</c>: why the draft is voided. Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VoidAdjustmentRequest
{
    /// <summary>The reason. Required; it is kept with the adjustment.</summary>
    public string? Reason { get; init; }

    /// <summary>The command for adjustment <paramref name="id"/>, at the version the client read.</summary>
    public VoidAdjustmentCommand ToCommand(AdjustmentId id, string expectedVersion) => new(id, expectedVersion, Reason ?? string.Empty);
}
