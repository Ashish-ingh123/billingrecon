using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// An amount that had been applied to a posted bill was given back, and what is owed on it rose again (BR-208,
/// BR-305): so far the reversal of an allocation of a payment. <c>BalanceDue</c> is what is owed afterwards; the
/// status of the bill follows it.
/// </summary>
public sealed record BillCreditReversed(BillId BillId, string DocumentNumber, Money Amount, Money BalanceDue) : IDomainEvent, IHasDocumentNumber;
