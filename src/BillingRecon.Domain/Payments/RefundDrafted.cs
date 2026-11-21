using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>
/// A refund was drafted from a payment or a credit note of the payer. The id is still empty here: it is assigned when
/// the refund joins the unit of work, so the audit record takes it from the entity.
/// </summary>
public sealed record RefundDrafted(PayerId PayerId, RefundSourceKind SourceKind, Guid SourceId, Money Amount, PaymentMethod Method, DateOnly RefundDate)
    : IDomainEvent;
