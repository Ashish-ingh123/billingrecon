using BillingRecon.Application.Common;
using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>Explicit mapping from the Reconciliation aggregates to their DTOs (CLAUDE.md section 5).</summary>
public static class ReconciliationMapping
{
    /// <summary>The settlement channel as the API returns it.</summary>
    public static SettlementChannelDto ToDto(this SettlementChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        return new SettlementChannelDto(
            Id: channel.Id.Value,
            Kind: channel.Kind,
            Name: channel.Name,
            AccountLabel: channel.AccountLabel,
            Gateway: channel.Gateway,
            BankChannelId: channel.BankChannelId?.Value,
            DateWindowDays: channel.DateWindowDays,
            Mapping: channel.Mapping is { } mapping ? ColumnMappingDto.Of(mapping) : null,
            Status: channel.Status,
            CreatedAtUtc: channel.CreatedAtUtc,
            UpdatedAtUtc: channel.UpdatedAtUtc,
            Version: RowVersionText.Of(channel));
    }

    /// <summary>The import as the API returns it.</summary>
    public static StatementImportDto ToDto(this StatementImport import)
    {
        ArgumentNullException.ThrowIfNull(import);

        return new StatementImportDto(
            Id: import.Id.Value,
            ChannelId: import.ChannelId.Value,
            FileName: import.FileName,
            FileHash: import.FileHash,
            RowsRead: import.RowsRead,
            LinesAdded: import.LinesAdded,
            LinesAlreadyHeld: import.LinesAlreadyHeld,
            RowsRefused: import.RowsRefused,
            FirstDate: import.FirstDate,
            LastDate: import.LastDate,
            CreatedAtUtc: import.CreatedAtUtc,
            CreatedByUserId: import.CreatedByUserId?.Value);
    }

    /// <summary>The line as the API returns it.</summary>
    public static StatementLineDto ToDto(this StatementLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new StatementLineDto(
            Id: line.Id.Value,
            ChannelId: line.ChannelId.Value,
            ImportId: line.ImportId.Value,
            RowNumber: line.RowNumber,
            TransactionDate: line.TransactionDate,
            ValueDate: line.ValueDate,
            Direction: line.Direction,
            Amount: line.Amount.Amount,
            Reference: line.Reference,
            Description: line.Description,
            Balance: line.Balance?.Amount,
            RawText: line.RawText,
            Status: line.Status,
            Version: RowVersionText.Of(line));
    }
}
