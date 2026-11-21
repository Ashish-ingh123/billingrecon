using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// Part of a posted credit note was applied to a bill of the same payer (BR-212, BR-213). <c>DocumentNumber</c> is
/// the number of the credit note; <c>AvailableAmount</c> is what it has left afterwards.
/// </summary>
public sealed record CreditNoteApplied(string DocumentNumber, BillId BillId, Money Amount, Money AvailableAmount) : IDomainEvent, IHasDocumentNumber;
