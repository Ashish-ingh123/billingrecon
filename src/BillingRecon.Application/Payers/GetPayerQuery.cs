using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payers;

/// <summary>Reads one payer of the tenant.</summary>
public sealed record GetPayerQuery(PayerId Id);
