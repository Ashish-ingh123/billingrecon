using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>A checker approved the closing of a shift with its difference: it is closed (BR-314, BR-315).</summary>
public sealed record CashierShiftClosingApproved(CashierShiftId CashierShiftId, Money Difference) : IDomainEvent;
