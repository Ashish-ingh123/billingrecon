using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Adjustments;

/// <summary>
/// An adjustment was approved and posted under its number (BR-502, BR-606): the bill is owed less by its amount.
/// <c>Reason</c> is why the bill was lowered, as the maker wrote it.
/// </summary>
public sealed record AdjustmentPosted(AdjustmentId AdjustmentId, string DocumentNumber, BillId BillId, PayerId PayerId, AdjustmentKind Kind, Money Amount, string Reason)
    : IDomainEvent, IHasDocumentNumber, IHasReason;
