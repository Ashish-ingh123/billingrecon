using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Reporting;

/// <summary>
/// The newest payments of the dashboard's period: the payment register of 6.1, newest first, its first page of
/// <see cref="DashboardRecentPaymentsQuery.Count"/>, with the register's totals of the period. Reused, not copied.
/// </summary>
public sealed class DashboardRecentPaymentsHandler(
    IValidator<DashboardRecentPaymentsQuery> validator,
    IQueryHandler<PaymentRegisterQuery, ReportPage<PaymentRegisterRowDto, PaymentRegisterTotalsDto>> register,
    IClock clock)
    : IQueryHandler<DashboardRecentPaymentsQuery, ReportPage<PaymentRegisterRowDto, PaymentRegisterTotalsDto>>
{
    /// <inheritdoc />
    public async Task<Result<ReportPage<PaymentRegisterRowDto, PaymentRegisterTotalsDto>>> HandleAsync(DashboardRecentPaymentsQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        var period = DashboardPeriods.Resolve(query.From, query.To, clock.TodayIst);
        if (period.IsFailure)
        {
            return period.Error;
        }

        return await register.HandleAsync(
            new PaymentRegisterQuery
            {
                From = period.Value.From,
                To = period.Value.To,
                Order = ReportOrder.NewestFirst,
                Page = Paging.FirstPage,
                PageSize = DashboardRecentPaymentsQuery.Count,
            },
            ct);
    }
}
