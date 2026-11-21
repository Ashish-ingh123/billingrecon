using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>A draft refund was given up, with the reason.</summary>
public sealed record RefundVoided(RefundId RefundId, Money Amount, string Reason) : IDomainEvent, IHasReason;
