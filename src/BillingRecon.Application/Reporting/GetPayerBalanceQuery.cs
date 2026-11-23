using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>One payer's outstanding, unapplied cash and available credit (US-40).</summary>
/// <param name="PayerId">The payer.</param>
public sealed record GetPayerBalanceQuery(PayerId PayerId);
