using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>The checker rejected the reversal of a payment (BR-605): the payment is as it was.</summary>
public sealed record PaymentReversalRejected(string DocumentNumber, string Reason) : IDomainEvent, IHasDocumentNumber, IHasReason;
