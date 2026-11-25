using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payers;

/// <summary>A guarantor the tenant has: the payer, its name, and whether it is active.</summary>
public sealed record KnownGuarantor(PayerId Id, string Name, bool Active);
