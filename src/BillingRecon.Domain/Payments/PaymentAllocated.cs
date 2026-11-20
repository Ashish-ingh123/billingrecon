using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>
/// Part of a payment was applied to a bill of the same payer (BR-303, US-31). <c>DocumentNumber</c> is the number of
/// the payment; <c>UnappliedAmount</c> is what the payment has left after this allocation.
/// </summary>
public sealed record PaymentAllocated(string DocumentNumber, BillId BillId, Money Amount, Money UnappliedAmount) : IDomainEvent, IHasDocumentNumber;
