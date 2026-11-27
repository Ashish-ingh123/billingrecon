using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>A shift was counted and its cash was as expected: it is closed (BR-313, BR-315).</summary>
public sealed record CashierShiftClosed(CashierShiftId CashierShiftId, Money ExpectedCash, Money CountedCash) : IDomainEvent;
