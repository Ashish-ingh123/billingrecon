using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>A draft refund was handed over to a second person (BR-604).</summary>
public sealed record RefundSubmittedForApproval(RefundId RefundId, Money Amount) : IDomainEvent;
