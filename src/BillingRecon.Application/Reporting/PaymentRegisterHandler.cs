using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Reporting;

/// <summary>The payment register: the payments of a period, the current month in India unless another is asked for.</summary>
public sealed class PaymentRegisterHandler(IValidator<PaymentRegisterQuery> validator, IRegisterReports reports, IClock clock)
    : IQueryHandler<PaymentRegisterQuery, ReportPage<PaymentRegisterRowDto, PaymentRegisterTotalsDto>>
{
    /// <inheritdoc />
    public async Task<Result<ReportPage<PaymentRegisterRowDto, PaymentRegisterTotalsDto>>> HandleAsync(PaymentRegisterQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        return await reports.PaymentRegisterAsync(ReportPeriods.Resolve(query.From, query.To, clock.TodayIst), query, ct);
    }
}
