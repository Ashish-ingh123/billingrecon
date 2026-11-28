using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Reconciliation;

/// <summary>
/// A settlement channel was set up (BR-1001). It carries no id: the id is assigned when the channel is added to the
/// unit of work, after the event was raised (ADR 0015, section 2).
/// </summary>
public sealed record SettlementChannelCreated(SettlementChannelKind Kind, string Name, PaymentGateway? Gateway) : IDomainEvent;
