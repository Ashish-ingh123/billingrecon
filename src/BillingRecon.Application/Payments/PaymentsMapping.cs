using BillingRecon.Application.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>Explicit mapping from the payment to its DTO (CLAUDE.md section 5).</summary>
public static class PaymentsMapping
{
    /// <summary>The payment as the API returns it.</summary>
    public static PaymentDto ToDto(this Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);

        return new PaymentDto(
            Id: payment.Id.Value,
            Number: payment.Number,
            FiscalYear: payment.FiscalYear.Label,
            PayerId: payment.PayerId.Value,
            PaymentDate: payment.PaymentDate,
            Method: payment.Method,
            Reference: payment.Reference,
            Amount: payment.Amount.Amount,
            UnappliedAmount: payment.UnappliedAmount.Amount,
            Status: payment.Status,
            Notes: payment.Notes,
            CreatedAtUtc: payment.CreatedAtUtc,
            UpdatedAtUtc: payment.UpdatedAtUtc,
            Version: RowVersionText.Of(payment));
    }
}
