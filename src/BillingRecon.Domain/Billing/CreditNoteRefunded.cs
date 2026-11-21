using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>Part of a credit note's available credit was paid back by refund <c>RefundNumber</c> (BR-213, BR-307).</summary>
public sealed record CreditNoteRefunded(string DocumentNumber, string RefundNumber, Money Amount, Money AvailableAmount) : IDomainEvent, IHasDocumentNumber;
