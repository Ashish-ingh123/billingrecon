using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reconciliation;

/// <summary>Runs automatic matching over the unmatched lines of a bank channel (BR-1010, BR-1011; ADR 0066).</summary>
public sealed record RunMatchingCommand(SettlementChannelId ChannelId);
