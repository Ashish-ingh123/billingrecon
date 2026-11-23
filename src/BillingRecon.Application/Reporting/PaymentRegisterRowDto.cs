namespace BillingRecon.Application.Reporting;

/// <summary>One payment in the payment register.</summary>
public sealed record PaymentRegisterRowDto(
    Guid PaymentId,
    string Number,
    DateOnly PaymentDate,
    Guid PayerId,
    string PayerName,
    ReportPaymentMethod Method,
    string? Reference,
    decimal Amount,
    decimal UnappliedAmount,
    ReportPaymentStatus Status,
    DateTimeOffset? ReversedAtUtc);
