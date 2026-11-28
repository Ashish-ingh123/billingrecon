using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;
using FluentValidation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// The dry run of a bank's statement (BR-1003): reads the file with the channel's mapping and says what the import would
/// add, what the channel already holds (BR-1004) and which rows it refuses and why. It stores nothing, so it takes no
/// Idempotency-Key. Its file hash is what the import sends back to import exactly this file.
/// </summary>
public sealed class DryRunStatementHandler(
    IValidator<DryRunStatementQuery> validator,
    ISettlementChannelReadService channels,
    IStatementRepository statements,
    IClock clock) : IQueryHandler<DryRunStatementQuery, StatementDryRunDto>
{
    /// <summary>How many of the lines read the answer shows; the counts and the money cover them all.</summary>
    public const int PreviewLimit = 1000;

    /// <inheritdoc />
    public async Task<Result<StatementDryRunDto>> HandleAsync(DryRunStatementQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        var channelId = new SettlementChannelId(query.ChannelId);
        var channel = await channels.GetAsync(channelId, ct);
        var mapping = StatementChannelCheck.MappingOf(channel?.Kind, channel?.Status ?? default, channel?.Mapping?.ToMapping());
        if (mapping.IsFailure)
        {
            return mapping.Error;
        }

        var reading = StatementReader.Read(query.Text, mapping.Value, clock.TodayIst);
        if (reading.UnreadableReason is { } reason)
        {
            return ReconciliationErrors.StatementFileUnreadable(reason);
        }

        var fileHash = StatementPlan.FileHashOf(query.Text);
        var alreadyImported = await statements.FileImportedAsync(channelId, fileHash, ct);
        var held = await statements.HeldCountsAsync(channelId, StatementPlan.Fingerprints(channelId, reading.Lines), ct);
        var planned = StatementPlan.Plan(channelId, reading.Lines, held);
        var added = planned.Where(line => !line.AlreadyHeld).ToList();

        return new StatementDryRunDto(
            FileHash: fileHash,
            AlreadyImported: alreadyImported,
            RowsRead: reading.RowsRead,
            LinesToAdd: added.Count,
            LinesAlreadyHeld: planned.Count - added.Count,
            RowsRefused: reading.Refusals.Count,
            MoneyIn: added.Where(line => line.Row.Values.Direction == StatementDirection.In).Sum(line => line.Row.Values.Amount.Amount),
            MoneyOut: added.Where(line => line.Row.Values.Direction == StatementDirection.Out).Sum(line => line.Row.Values.Amount.Amount),
            FirstDate: planned.Count == 0 ? null : planned.Min(line => line.Row.Values.TransactionDate),
            LastDate: planned.Count == 0 ? null : planned.Max(line => line.Row.Values.TransactionDate),
            Refusals: reading.Refusals,
            Lines: [.. planned.Take(PreviewLimit).Select(Preview)]);
    }

    private static StatementPreviewLineDto Preview(PlannedStatementLine line)
    {
        var values = line.Row.Values;
        return new StatementPreviewLineDto(
            line.Row.RowNumber,
            values.TransactionDate,
            values.ValueDate,
            values.Direction,
            values.Amount.Amount,
            values.Reference,
            values.Description,
            values.Balance?.Amount,
            line.AlreadyHeld);
    }
}
