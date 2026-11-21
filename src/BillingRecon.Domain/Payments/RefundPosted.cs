using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>A refund was approved and posted under its number (BR-307, BR-606): the money left the hospital.</summary>
public sealed record RefundPosted(RefundId RefundId, string DocumentNumber, PayerId PayerId, RefundSourceKind SourceKind, Guid SourceId, Money Amount, PaymentMethod Method, string Reason)
    : IDomainEvent, IHasDocumentNumber, IHasReason;
