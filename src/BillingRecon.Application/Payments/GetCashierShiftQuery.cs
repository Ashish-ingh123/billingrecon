using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>Reads one shift.</summary>
public sealed record GetCashierShiftQuery(CashierShiftId Id);
