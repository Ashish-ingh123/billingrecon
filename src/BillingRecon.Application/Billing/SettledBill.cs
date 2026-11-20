using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>What <see cref="IBillSettlement"/> answers: the number of the bill, and what is still owed on it after the amount was applied.</summary>
public sealed record SettledBill(string Number, Money BalanceDue);
