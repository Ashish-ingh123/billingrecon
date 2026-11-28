using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Reconciliation;

/// <summary>An inactive settlement channel was made active again.</summary>
public sealed record SettlementChannelReactivated(SettlementChannelId SettlementChannelId, string Name) : IDomainEvent;
