using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;
using FluentValidation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// Imports a bank's statement (BR-1003, BR-1004; ADR 0065). The file must be the one the dry run read, a file the
/// channel took before is refused, and a line the channel already holds is not stored again. The import and its lines
/// are saved in one transaction; the unique indexes on the file's hash and on a line's fingerprint and occurrence decide a
/// race between two imports, whose loser is told the file was already imported.
/// </summary>
public sealed class ImportStatementHandler(
    IValidator<ImportStatementCommand> validator,
    ISettlementChannelRepository channels,
    IStatementRepository statements,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<ImportStatementCommand, StatementImportDto>
{
    /// <inheritdoc />
    public async Task<Result<StatementImportDto>> HandleAsync(ImportStatementCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var channelId = new SettlementChannelId(command.ChannelId);
        var channel = await channels.GetAsync(channelId, ct);
        var mapping = StatementChannelCheck.MappingOf(channel?.Kind, channel?.Status ?? default, channel?.Mapping);
        if (mapping.IsFailure)
        {
            return mapping.Error;
        }

        var fileHash = StatementPlan.FileHashOf(command.Text);
        if (!string.Equals(fileHash, command.ExpectedFileHash, StringComparison.OrdinalIgnoreCase))
        {
            return ReconciliationErrors.StatementFileChanged();
        }

        if (await statements.FileImportedAsync(channelId, fileHash, ct))
        {
            return ReconciliationErrors.StatementFileAlreadyImported();
        }

        var reading = StatementReader.Read(command.Text, mapping.Value, clock.TodayIst);
        if (reading.UnreadableReason is { } reason)
        {
            return ReconciliationErrors.StatementFileUnreadable(reason);
        }

        var held = await statements.HeldCountsAsync(channelId, StatementPlan.Fingerprints(channelId, reading.Lines), ct);
        var planned = StatementPlan.Plan(channelId, reading.Lines, held);
        var added = planned.Where(line => !line.AlreadyHeld).ToList();

        var import = StatementImport.Record(
            channelId,
            command.FileName.Trim(),
            fileHash,
            rowsRead: reading.RowsRead,
            linesAdded: added.Count,
            linesAlreadyHeld: planned.Count - added.Count,
            rowsRefused: reading.Refusals.Count,
            firstDate: planned.Count == 0 ? null : planned.Min(line => line.Row.Values.TransactionDate),
            lastDate: planned.Count == 0 ? null : planned.Max(line => line.Row.Values.TransactionDate));
        statements.Add(import);
        statements.AddLines([.. added.Select(line => StatementLine.Read(channelId, import.Id, line.Row.RowNumber, line.Row.Values, line.Row.RawText, line.Occurrence))]);

        var committed = await unitOfWork.CommitAsync(ct);
        if (committed.IsFailure)
        {
            // Another import of this file, or of these lines, won the race.
            return committed.Error.Code == CommonErrors.DuplicateKeyCode ? ReconciliationErrors.StatementFileAlreadyImported() : committed.Error;
        }

        return import.ToDto();
    }
}
