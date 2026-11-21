using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>Part of what a payment had unapplied was paid back by refund <c>RefundNumber</c> (BR-307).</summary>
public sealed record PaymentRefunded(string DocumentNumber, string RefundNumber, Money Amount, Money UnappliedAmount)
    : IDomainEvent, IHasDocumentNumber;
