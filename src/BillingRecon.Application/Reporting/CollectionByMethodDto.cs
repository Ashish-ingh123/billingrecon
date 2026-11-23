namespace BillingRecon.Application.Reporting;

/// <summary>
/// One payment method on one business date: the payments received that day and what they came to, the payments of this
/// method reversed that day, the refunds paid this way that day, and what is left. <see cref="Net"/> is
/// <see cref="Received"/> less <see cref="Reversed"/> and <see cref="Refunded"/>.
/// </summary>
public sealed record CollectionByMethodDto(
    ReportPaymentMethod Method,
    int Payments,
    decimal Received,
    decimal Reversed,
    decimal Refunded,
    decimal Net);
