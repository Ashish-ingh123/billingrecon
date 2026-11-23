namespace BillingRecon.Application.Reporting;

/// <summary>
/// The payment register's totals over the whole period and filter: how many payments, what they brought in, and what of
/// it is not applied to a bill. <see cref="Amount"/> equals the debits to Cash of their postings (BR-901).
/// </summary>
public sealed record PaymentRegisterTotalsDto(int Payments, decimal Amount, decimal UnappliedAmount);
