using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Adjustments;

/// <summary>A draft adjustment was voided: it keeps its content, and has no number and no ledger effect.</summary>
public sealed record AdjustmentVoided(AdjustmentId AdjustmentId, BillId BillId, Money Amount, string Reason) : IDomainEvent, IHasReason;
