using BillingRecon.Application.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>Maps a refund to what the API answers.</summary>
public static class RefundsMapping
{
    /// <summary>The refund as a <see cref="RefundDto"/>, with its row version as text.</summary>
    public static RefundDto ToDto(this Refund refund)
    {
        ArgumentNullException.ThrowIfNull(refund);

        return new RefundDto(
            Id: refund.Id.Value,
            PayerId: refund.PayerId.Value,
            SourceKind: refund.SourceKind,
            PaymentId: refund.SourcePaymentId?.Value,
            CreditNoteId: refund.SourceCreditNoteId?.Value,
            RefundDate: refund.RefundDate,
            Amount: refund.Amount.Amount,
            Method: refund.Method,
            Reference: refund.Reference,
            Reason: refund.Reason,
            Status: refund.Status,
            Number: refund.Number,
            FiscalYear: refund.FiscalYear?.Label,
            PostedAtUtc: refund.PostedAtUtc,
            VoidedAtUtc: refund.VoidedAtUtc,
            VoidReason: refund.VoidReason,
            CreatedAtUtc: refund.CreatedAtUtc,
            UpdatedAtUtc: refund.UpdatedAtUtc,
            Version: RowVersionText.Of(refund));
    }
}
