using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>A checker rejected the closing of a shift (BR-605): it is open again, to be counted again.</summary>
public sealed record CashierShiftClosingRejected(CashierShiftId CashierShiftId, string Reason) : IDomainEvent, IHasReason;
