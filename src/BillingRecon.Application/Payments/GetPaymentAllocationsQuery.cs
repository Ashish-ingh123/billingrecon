using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>Reads a payment with its allocations.</summary>
public sealed record GetPaymentAllocationsQuery(PaymentId Id);
