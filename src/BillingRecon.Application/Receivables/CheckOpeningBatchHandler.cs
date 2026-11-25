using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Receivables;

/// <summary>
/// The dry run of an opening batch (ADR 0053): what the files would bring in, and every row that stops them, with its
/// file, line and reason. It reads and never writes.
/// </summary>
public sealed class CheckOpeningBatchHandler(
    IValidator<CheckOpeningBatchQuery> validator,
    IImportParties parties,
    IOpeningReceivables openingReceivables,
    IOpeningAdvances openingAdvances) : IQueryHandler<CheckOpeningBatchQuery, OpeningBatchCheckDto>
{
    /// <inheritdoc />
    public async Task<Result<OpeningBatchCheckDto>> HandleAsync(CheckOpeningBatchQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        var cutOverDate = query.CutOverDate!.Value;
        var files = new OpeningBatchFiles(cutOverDate, query.BillsCsv, query.AdvancesCsv, query.TrialBalanceCsv);
        var check = await new OpeningBatchChecker(parties, openingReceivables, openingAdvances).CheckAsync(files, ct);

        return check.ToDto(cutOverDate);
    }
}
