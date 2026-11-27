using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>A shift was counted with a difference, and its closing waits for a checker (BR-313, BR-314).</summary>
public sealed record CashierShiftSubmittedForApproval(CashierShiftId CashierShiftId, Money ExpectedCash, Money CountedCash, Money Difference, string Reason) : IDomainEvent, IHasReason;
