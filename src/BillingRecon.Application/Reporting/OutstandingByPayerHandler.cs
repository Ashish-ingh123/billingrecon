using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Reporting;

/// <summary>The payers with a balance, the largest net outstanding first (BR-901).</summary>
public sealed class OutstandingByPayerHandler(IValidator<OutstandingByPayerQuery> validator, IBalanceReports reports)
    : IQueryHandler<OutstandingByPayerQuery, ReportPage<PayerOutstandingDto, OutstandingTotalsDto>>
{
    /// <inheritdoc />
    public async Task<Result<ReportPage<PayerOutstandingDto, OutstandingTotalsDto>>> HandleAsync(OutstandingByPayerQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        return await reports.OutstandingByPayerAsync(query, ct);
    }
}
