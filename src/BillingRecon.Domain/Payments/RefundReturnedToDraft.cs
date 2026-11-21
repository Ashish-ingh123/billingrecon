using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>The checker rejected a refund (BR-605); it is a draft again.</summary>
public sealed record RefundReturnedToDraft(RefundId RefundId, string Reason) : IDomainEvent, IHasReason;
