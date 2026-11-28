using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Reconciliation;

/// <summary>A settlement channel was deactivated: it takes no import (BR-1001).</summary>
public sealed record SettlementChannelDeactivated(SettlementChannelId SettlementChannelId, string Name) : IDomainEvent;
