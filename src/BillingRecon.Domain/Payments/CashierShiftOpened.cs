using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>A user opened a shift with a float (BR-310; ADR 0060).</summary>
public sealed record CashierShiftOpened(UserId CashierUserId, DateOnly BusinessDate, Money OpeningFloat) : IDomainEvent;
