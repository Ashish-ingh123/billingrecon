using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>A payment was reversed (BR-306): the money is no longer held for the payer.</summary>
public sealed record PaymentReversed(string DocumentNumber, PayerId PayerId, Money Amount, string Reason) : IDomainEvent, IHasDocumentNumber, IHasReason;
