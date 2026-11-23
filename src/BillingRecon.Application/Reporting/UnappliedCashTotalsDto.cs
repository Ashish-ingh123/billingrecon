namespace BillingRecon.Application.Reporting;

/// <summary>Every payment of the report together; <see cref="UnappliedAmount"/> equals the balance of the UnappliedCash account (BR-901).</summary>
public sealed record UnappliedCashTotalsDto(int Payments, decimal UnappliedAmount);
