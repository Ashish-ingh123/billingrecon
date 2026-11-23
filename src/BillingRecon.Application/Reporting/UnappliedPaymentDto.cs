namespace BillingRecon.Application.Reporting;

/// <summary>A payment that still holds money not applied to a bill (BR-304).</summary>
public sealed record UnappliedPaymentDto(
    Guid PaymentId,
    string Number,
    DateOnly PaymentDate,
    Guid PayerId,
    string PayerName,
    ReportPaymentMethod Method,
    decimal Amount,
    decimal UnappliedAmount,
    ReportPaymentStatus Status);
