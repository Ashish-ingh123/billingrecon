using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>
/// One payer's balances (US-40). The payer is found through the Payers module's own query, so an id nobody has, or
/// another tenant's, answers what the payer endpoints answer: 404 <c>payer_not_found</c>.
/// </summary>
public sealed class GetPayerBalanceHandler(IQueryHandler<GetPayerQuery, PayerDto> payers, IBalanceReports reports)
    : IQueryHandler<GetPayerBalanceQuery, PayerOutstandingDto>
{
    /// <inheritdoc />
    public async Task<Result<PayerOutstandingDto>> HandleAsync(GetPayerBalanceQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var payer = await payers.HandleAsync(new GetPayerQuery(query.PayerId), ct);
        if (payer.IsFailure)
        {
            return payer.Error;
        }

        return await reports.PayerBalanceAsync(query.PayerId, payer.Value.Name, ct);
    }
}
