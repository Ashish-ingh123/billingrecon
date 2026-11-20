using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>Reads one payment of the tenant.</summary>
public sealed record GetPaymentQuery(PaymentId Id);
