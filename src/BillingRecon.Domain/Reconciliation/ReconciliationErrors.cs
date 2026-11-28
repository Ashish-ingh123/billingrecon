using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Reconciliation;

/// <summary>The failures of the Reconciliation module (R2). The codes are part of the API contract (docs/api.md).</summary>
public static class ReconciliationErrors
{
    public const string SettlementChannelNotFoundCode = "settlement_channel_not_found";
    public const string SettlementChannelNameDuplicateCode = "settlement_channel_name_duplicate";
    public const string SettlementChannelAlreadyInactiveCode = "settlement_channel_already_inactive";
    public const string SettlementChannelAlreadyActiveCode = "settlement_channel_already_active";
    public const string SettlementChannelInactiveCode = "settlement_channel_inactive";
    public const string SettlementChannelNotBankCode = "settlement_channel_not_bank";
    public const string SettlementChannelBankInvalidCode = "settlement_channel_bank_invalid";
    public const string StatementImportNotFoundCode = "statement_import_not_found";
    public const string StatementFileAlreadyImportedCode = "statement_file_already_imported";
    public const string StatementFileChangedCode = "statement_file_changed";
    public const string StatementFileUnreadableCode = "statement_file_unreadable";
    public const string MatchingRunInProgressCode = "matching_run_in_progress";

    /// <summary>No channel of the hospital has that id.</summary>
    public static Error SettlementChannelNotFound() => Error.NotFound(SettlementChannelNotFoundCode, "The settlement channel does not exist.");

    /// <summary>The hospital already has a channel of that name, active or not (BR-1001).</summary>
    public static Error SettlementChannelNameDuplicate()
        => Error.Conflict(SettlementChannelNameDuplicateCode, "A settlement channel with this name already exists.");

    /// <summary>Deactivating a channel that is already inactive.</summary>
    public static Error SettlementChannelAlreadyInactive()
        => Error.BusinessRule(SettlementChannelAlreadyInactiveCode, "The settlement channel is already inactive.");

    /// <summary>Reactivating a channel that is already active.</summary>
    public static Error SettlementChannelAlreadyActive()
        => Error.BusinessRule(SettlementChannelAlreadyActiveCode, "The settlement channel is already active.");

    /// <summary>An import into a deactivated channel (BR-1001).</summary>
    public static Error SettlementChannelInactive()
        => Error.BusinessRule(SettlementChannelInactiveCode, "The settlement channel is inactive and takes no import.");

    /// <summary>A bank's statement or a column mapping for a gateway channel: a gateway sends its settlement report (BR-1019).</summary>
    public static Error SettlementChannelNotBank()
        => Error.BusinessRule(SettlementChannelNotBankCode, "Only a bank channel has a column mapping and takes a bank's statement.");

    /// <summary>A gateway channel names a channel that is not an active bank channel of the hospital (BR-1001).</summary>
    public static Error SettlementChannelBankInvalid()
        => Error.BusinessRule(SettlementChannelBankInvalidCode, "A gateway settles into an active bank channel of the hospital.");

    /// <summary>No import of the hospital has that id.</summary>
    public static Error StatementImportNotFound() => Error.NotFound(StatementImportNotFoundCode, "The statement import does not exist.");

    /// <summary>The same file was imported into the channel before; nothing is added (BR-1004).</summary>
    public static Error StatementFileAlreadyImported()
        => Error.Conflict(StatementFileAlreadyImportedCode, "This file was already imported into this channel; nothing was added.");

    /// <summary>The file sent to the import is not the one its dry run read (BR-1003).</summary>
    public static Error StatementFileChanged()
        => Error.Conflict(StatementFileChangedCode, "The file differs from the one the dry run read. Run the dry run again.");

    /// <summary>The file cannot be read as a whole: no header, a missing column, too large, not UTF-8 (BR-1003).</summary>
    public static Error StatementFileUnreadable(string reason)
        => Error.Validation(StatementFileUnreadableCode, $"The file cannot be read: {reason}");

    /// <summary>A matching run of the channel is under way; one runs at a time (BR-1029).</summary>
    public static Error MatchingRunInProgress()
        => Error.Conflict(MatchingRunInProgressCode, "A matching run of this channel is under way. Wait for it and look at its result.");
}
