using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Adjustments;

/// <summary>
/// A draft adjustment was created against a posted bill (BR-501). It carries no adjustment id: the id is assigned
/// when the adjustment is added to the unit of work, after the event was raised.
/// </summary>
public sealed record AdjustmentDrafted(BillId BillId, PayerId PayerId, AdjustmentKind Kind, Money Amount, DateOnly AdjustmentDate) : IDomainEvent;
