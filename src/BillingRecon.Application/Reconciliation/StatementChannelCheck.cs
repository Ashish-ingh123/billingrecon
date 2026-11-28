using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>Whether a channel takes a bank's statement (BR-1001): it exists in the hospital, is a bank, is active and has its mapping.</summary>
internal static class StatementChannelCheck
{
    /// <summary>The channel's mapping, or why the channel takes no statement.</summary>
    public static Result<ColumnMapping> MappingOf(SettlementChannelKind? kind, SettlementChannelStatus status, ColumnMapping? mapping)
    {
        if (kind is null)
        {
            return ReconciliationErrors.SettlementChannelNotFound();
        }

        if (kind != SettlementChannelKind.Bank || mapping is not { } found)
        {
            return ReconciliationErrors.SettlementChannelNotBank();
        }

        return status == SettlementChannelStatus.Active ? found : ReconciliationErrors.SettlementChannelInactive();
    }
}
