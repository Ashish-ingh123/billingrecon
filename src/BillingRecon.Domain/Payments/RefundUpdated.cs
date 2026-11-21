using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>A draft refund was replaced with new values.</summary>
public sealed record RefundUpdated(RefundId RefundId, Money Amount, PaymentMethod Method, DateOnly RefundDate)
    : IDomainEvent;
