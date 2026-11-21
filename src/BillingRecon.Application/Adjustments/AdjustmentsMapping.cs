using BillingRecon.Application.Common;
using BillingRecon.Domain.Adjustments;

namespace BillingRecon.Application.Adjustments;

/// <summary>Explicit mapping from the adjustment aggregate to what the API returns (CLAUDE.md section 5).</summary>
public static class AdjustmentsMapping
{
    /// <summary>The adjustment as the API returns it.</summary>
    public static AdjustmentDto ToDto(this Adjustment adjustment)
    {
        ArgumentNullException.ThrowIfNull(adjustment);

        return new AdjustmentDto(
            Id: adjustment.Id.Value,
            BillId: adjustment.BillId.Value,
            PayerId: adjustment.PayerId.Value,
            AdjustmentDate: adjustment.AdjustmentDate,
            Kind: adjustment.Kind,
            Amount: adjustment.Amount.Amount,
            Reason: adjustment.Reason,
            Status: adjustment.Status,
            Number: adjustment.Number,
            FiscalYear: adjustment.FiscalYear?.Label,
            PostedAtUtc: adjustment.PostedAtUtc,
            VoidedAtUtc: adjustment.VoidedAtUtc,
            VoidReason: adjustment.VoidReason,
            CreatedAtUtc: adjustment.CreatedAtUtc,
            UpdatedAtUtc: adjustment.UpdatedAtUtc,
            Version: RowVersionText.Of(adjustment));
    }
}
