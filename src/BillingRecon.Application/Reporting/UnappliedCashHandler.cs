using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Reporting;

/// <summary>The payments that hold unapplied cash, the oldest first (BR-304, BR-901).</summary>
public sealed class UnappliedCashHandler(IValidator<UnappliedCashQuery> validator, IBalanceReports reports)
    : IQueryHandler<UnappliedCashQuery, ReportPage<UnappliedPaymentDto, UnappliedCashTotalsDto>>
{
    /// <inheritdoc />
    public async Task<Result<ReportPage<UnappliedPaymentDto, UnappliedCashTotalsDto>>> HandleAsync(UnappliedCashQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        return await reports.UnappliedCashAsync(query, ct);
    }
}
