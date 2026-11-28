using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reconciliation;

/// <summary>Reads one settlement channel of the tenant.</summary>
public sealed record GetSettlementChannelQuery(SettlementChannelId Id);
