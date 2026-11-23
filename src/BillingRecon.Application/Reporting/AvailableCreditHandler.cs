using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Reporting;

/// <summary>The posted credit notes that hold available credit, the oldest first (BR-901).</summary>
public sealed class AvailableCreditHandler(IValidator<AvailableCreditQuery> validator, IBalanceReports reports)
    : IQueryHandler<AvailableCreditQuery, ReportPage<AvailableCreditNoteDto, AvailableCreditTotalsDto>>
{
    /// <inheritdoc />
    public async Task<Result<ReportPage<AvailableCreditNoteDto, AvailableCreditTotalsDto>>> HandleAsync(AvailableCreditQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        return await reports.AvailableCreditAsync(query, ct);
    }
}
