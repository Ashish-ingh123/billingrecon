using System.Text.Json.Serialization;
using BillingRecon.Application.Adjustments;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Adjustments;

/// <summary>The body of <c>PUT /api/v1/adjustments/{id}</c>: everything a draft adjustment says, replaced as a whole. Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateAdjustmentRequest
{
    /// <summary>The business date of the adjustment. Required.</summary>
    public DateOnly? AdjustmentDate { get; init; }

    /// <summary><c>Discount</c>, <c>WriteOff</c> or <c>Rounding</c>. Required.</summary>
    public AdjustmentKind? Kind { get; init; }

    /// <summary>By how much the bill is lowered. Required.</summary>
    public decimal? Amount { get; init; }

    /// <summary>Why the bill is lowered. Required.</summary>
    public string? Reason { get; init; }

    /// <summary>The command for adjustment <paramref name="id"/>, at the version the client read.</summary>
    public UpdateAdjustmentCommand ToCommand(AdjustmentId id, string expectedVersion) => new(id, expectedVersion, AdjustmentDate, Kind, Amount, Reason ?? string.Empty);
}
