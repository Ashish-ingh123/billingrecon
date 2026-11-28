using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Reconciliation;

/// <summary>The name, the label, the window or the bank of a settlement channel changed (BR-1001).</summary>
public sealed record SettlementChannelChanged(SettlementChannelId SettlementChannelId, string Name) : IDomainEvent;
