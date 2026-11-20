using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// An amount was applied to a posted bill and lowered what is owed on it (BR-208, BR-303): so far the allocation of a
/// payment. <c>BalanceDue</c> is what is still owed afterwards; the status of the bill follows it.
/// </summary>
public sealed record BillCreditApplied(BillId BillId, string DocumentNumber, Money Amount, Money BalanceDue) : IDomainEvent, IHasDocumentNumber;
