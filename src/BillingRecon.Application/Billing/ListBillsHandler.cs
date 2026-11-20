using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>Lists the tenant's bills one page at a time, the newest bill date first.</summary>
public sealed class ListBillsHandler(IValidator<ListBillsQuery> validator, IBillReadService bills)
    : IQueryHandler<ListBillsQuery, PageOf<BillSummaryDto>>
{
    /// <inheritdoc />
    public async Task<Result<PageOf<BillSummaryDto>>> HandleAsync(ListBillsQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        return await bills.ListAsync(query, ct);
    }
}
