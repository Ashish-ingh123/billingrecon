using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>
/// An allocation was reversed (BR-305): its amount is unapplied again. <c>UnappliedAmount</c> is what the payment has
/// unapplied afterwards. Raised for an allocation reversed on its own and for each one a payment reversal takes back.
/// </summary>
public sealed record PaymentAllocationReversed(string DocumentNumber, PaymentAllocationId AllocationId, BillId BillId, Money Amount, Money UnappliedAmount, string Reason)
    : IDomainEvent, IHasDocumentNumber, IHasReason;
