using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>The checker rejected the reversal of an allocation (BR-605): the allocation is in force as before.</summary>
public sealed record PaymentAllocationReversalRejected(string DocumentNumber, PaymentAllocationId AllocationId, string Reason)
    : IDomainEvent, IHasDocumentNumber, IHasReason;
