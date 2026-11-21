using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>A cashier asked for a payment to be reversed (BR-306, US-33). Nothing financial has happened yet.</summary>
public sealed record PaymentReversalRequested(string DocumentNumber, Money Amount, string Reason) : IDomainEvent, IHasDocumentNumber, IHasReason;
