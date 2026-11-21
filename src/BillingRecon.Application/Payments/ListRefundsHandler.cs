using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>Lists the refunds of the tenant, a page at a time.</summary>
public sealed class ListRefundsHandler(IValidator<ListRefundsQuery> validator, IRefundReadService refunds)
    : IQueryHandler<ListRefundsQuery, PageOf<RefundDto>>
{
    /// <inheritdoc />
    public async Task<Result<PageOf<RefundDto>>> HandleAsync(ListRefundsQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        return await refunds.ListAsync(query, ct);
    }
}
