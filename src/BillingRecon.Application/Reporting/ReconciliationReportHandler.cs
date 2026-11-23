using BillingRecon.Application.Common;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Reporting;

/// <summary>
/// The reconciliation report (US-41): the drift rows of the Receivables module's reconciliation, through its Application
/// contract, ordered by identity and reference, one page at a time, with the count of every page and of each identity.
/// It reads and never corrects (docs/database.md section 5.6: a drift row is a defect, fixed in the code that made it).
/// </summary>
public sealed class ReconciliationReportHandler(IValidator<ReconciliationReportQuery> validator, IReconciliationQuery reconciliation, IClock clock)
    : IQueryHandler<ReconciliationReportQuery, ReportPage<DriftRowDto, ReconciliationTotalsDto>>
{
    /// <inheritdoc />
    public async Task<Result<ReportPage<DriftRowDto, ReconciliationTotalsDto>>> HandleAsync(ReconciliationReportQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        var checkedAtUtc = clock.UtcNow;
        var drift = (await reconciliation.FindDriftAsync(ct))
            .OrderBy(row => row.Identity, StringComparer.Ordinal)
            .ThenBy(row => row.Reference, StringComparer.Ordinal)
            .ThenBy(row => row.SubjectId)
            .ToList();

        DriftCountDto[] byIdentity =
        [
            .. drift.GroupBy(row => row.Identity, StringComparer.Ordinal).Select(group => new DriftCountDto(group.Key, group.Count())),
        ];

        DriftRowDto[] items =
        [
            .. drift
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(row => new DriftRowDto(row.Identity, row.SubjectId, row.Reference, row.Cached, row.Ledger)),
        ];

        return new ReportPage<DriftRowDto, ReconciliationTotalsDto>(
            null, items, query.Page, query.PageSize, drift.Count, new ReconciliationTotalsDto(checkedAtUtc, drift.Count, byIdentity));
    }
}
