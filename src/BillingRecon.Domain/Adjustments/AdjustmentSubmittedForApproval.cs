using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Adjustments;

/// <summary>A draft adjustment was handed to a second person (BR-502, BR-604). It has no number yet.</summary>
public sealed record AdjustmentSubmittedForApproval(AdjustmentId AdjustmentId, BillId BillId, Money Amount) : IDomainEvent;
