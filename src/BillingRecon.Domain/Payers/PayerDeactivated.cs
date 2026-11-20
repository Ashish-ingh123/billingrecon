using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payers;

/// <summary>A payer was deactivated (BR-105): no new bill, payment or refund for it.</summary>
public sealed record PayerDeactivated(PayerId PayerId, PayerKind Kind, string Name) : IDomainEvent;
