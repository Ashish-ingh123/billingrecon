using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Reconciliation;

/// <summary>The column mapping of a bank channel changed (BR-1002); lines imported before keep what was read.</summary>
public sealed record SettlementChannelMappingChanged(SettlementChannelId SettlementChannelId, string Name) : IDomainEvent;
