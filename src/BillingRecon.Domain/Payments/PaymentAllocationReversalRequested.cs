using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>
/// A cashier asked for one allocation of a payment to be reversed (BR-305, US-32). <c>DocumentNumber</c> is the
/// number of the payment. Nothing financial has happened yet.
/// </summary>
public sealed record PaymentAllocationReversalRequested(string DocumentNumber, PaymentAllocationId AllocationId, BillId BillId, Money Amount, string Reason)
    : IDomainEvent, IHasDocumentNumber, IHasReason;
