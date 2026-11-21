using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Adjustments;

/// <summary>Lists the tenant's adjustments one page at a time, the newest date first.</summary>
public sealed class ListAdjustmentsHandler(IValidator<ListAdjustmentsQuery> validator, IAdjustmentReadService adjustments)
    : IQueryHandler<ListAdjustmentsQuery, PageOf<AdjustmentDto>>
{
    /// <inheritdoc />
    public async Task<Result<PageOf<AdjustmentDto>>> HandleAsync(ListAdjustmentsQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        return await adjustments.ListAsync(query, ct);
    }
}
