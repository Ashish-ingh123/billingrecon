using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>Who recorded the payment, by name (ADR 0059).</summary>
public sealed record GetPaymentRecorderQuery(PaymentId Id);
