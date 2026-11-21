using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Adjustments;

/// <summary>The date, the kind, the amount or the reason of a draft adjustment was corrected. Raised only when a value really changed.</summary>
public sealed record AdjustmentUpdated(AdjustmentId AdjustmentId, AdjustmentKind Kind, Money Amount, DateOnly AdjustmentDate) : IDomainEvent;
