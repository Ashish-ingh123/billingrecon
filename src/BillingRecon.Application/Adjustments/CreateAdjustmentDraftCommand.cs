using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Adjustments;

/// <summary>Creates a draft adjustment against a posted bill (BR-501). A date left out is today in India.</summary>
public sealed record CreateAdjustmentDraftCommand
{
    /// <summary>The posted bill the adjustment lowers.</summary>
    public required BillId BillId { get; init; }

    /// <summary>The business date of the adjustment; today in India when null.</summary>
    public DateOnly? AdjustmentDate { get; init; }

    /// <summary>Discount, WriteOff or Rounding; null when the client sent none.</summary>
    public AdjustmentKind? Kind { get; init; }

    /// <summary>By how much the bill is lowered; null when the client sent none.</summary>
    public decimal? Amount { get; init; }

    /// <summary>Why the bill is lowered.</summary>
    public required string Reason { get; init; }
}
