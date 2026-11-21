using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Adjustments;

/// <summary>The checker rejected an adjustment that waited for approval (BR-605): it is a draft again.</summary>
public sealed record AdjustmentReturnedToDraft(AdjustmentId AdjustmentId, string Reason) : IDomainEvent, IHasReason;
