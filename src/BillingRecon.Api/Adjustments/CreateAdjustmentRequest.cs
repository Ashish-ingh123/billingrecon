using System.Text.Json.Serialization;
using BillingRecon.Application.Adjustments;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Adjustments;

/// <summary>The body of <c>POST /api/v1/adjustments</c>: a draft adjustment against a posted bill (BR-501). Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAdjustmentRequest
{
    /// <summary>The posted bill the adjustment lowers. Cannot be changed later.</summary>
    public Guid? BillId { get; init; }

    /// <summary>The business date of the adjustment; today in India when left out. Not before the bill date.</summary>
    public DateOnly? AdjustmentDate { get; init; }

    /// <summary><c>Discount</c>, <c>WriteOff</c> or <c>Rounding</c>. Required.</summary>
    public AdjustmentKind? Kind { get; init; }

    /// <summary>By how much the bill is lowered: above zero, at most two decimals; less than 1.00 for a rounding. Required.</summary>
    public decimal? Amount { get; init; }

    /// <summary>Why the bill is lowered. Required.</summary>
    public string? Reason { get; init; }

    /// <summary>The command; a missing id becomes the empty id and a missing reason an empty one, which the validator reports.</summary>
    public CreateAdjustmentDraftCommand ToCommand() => new()
    {
        BillId = new BillId(BillId ?? Guid.Empty),
        AdjustmentDate = AdjustmentDate,
        Kind = Kind,
        Amount = Amount,
        Reason = Reason ?? string.Empty,
    };
}
