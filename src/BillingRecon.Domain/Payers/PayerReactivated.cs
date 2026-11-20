using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payers;

/// <summary>An inactive payer was made active again.</summary>
public sealed record PayerReactivated(PayerId PayerId, PayerKind Kind, string Name) : IDomainEvent;
