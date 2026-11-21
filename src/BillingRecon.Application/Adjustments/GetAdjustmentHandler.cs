using BillingRecon.Application.Common;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Adjustments;

/// <summary>Reads one adjustment. Another tenant's adjustment is not found, like an id nobody has (BR-002).</summary>
public sealed class GetAdjustmentHandler(IAdjustmentReadService adjustments) : IQueryHandler<GetAdjustmentQuery, AdjustmentDto>
{
    /// <inheritdoc />
    public async Task<Result<AdjustmentDto>> HandleAsync(GetAdjustmentQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var adjustment = await adjustments.GetAsync(query.Id, ct);

        return adjustment is null ? AdjustmentsErrors.AdjustmentNotFound() : adjustment;
    }
}
