using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reconciliation;

/// <summary>Makes an inactive settlement channel active again. <see cref="ExpectedVersion"/> is the version the client read.</summary>
public sealed record ReactivateSettlementChannelCommand(SettlementChannelId Id, string ExpectedVersion);
