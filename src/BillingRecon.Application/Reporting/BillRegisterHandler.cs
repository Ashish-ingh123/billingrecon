using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Reporting;

/// <summary>The bill register: the posted bills of a period, the current month in India unless another is asked for.</summary>
public sealed class BillRegisterHandler(IValidator<BillRegisterQuery> validator, IRegisterReports reports, IClock clock)
    : IQueryHandler<BillRegisterQuery, ReportPage<BillRegisterRowDto, BillRegisterTotalsDto>>
{
    /// <inheritdoc />
    public async Task<Result<ReportPage<BillRegisterRowDto, BillRegisterTotalsDto>>> HandleAsync(BillRegisterQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        return await reports.BillRegisterAsync(ReportPeriods.Resolve(query.From, query.To, clock.TodayIst), query, ct);
    }
}
