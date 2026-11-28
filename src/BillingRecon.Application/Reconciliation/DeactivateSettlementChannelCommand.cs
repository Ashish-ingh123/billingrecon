using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reconciliation;

/// <summary>Deactivates a settlement channel: it takes no import (BR-1001). <see cref="ExpectedVersion"/> is the version the client read.</summary>
public sealed record DeactivateSettlementChannelCommand(SettlementChannelId Id, string ExpectedVersion);
