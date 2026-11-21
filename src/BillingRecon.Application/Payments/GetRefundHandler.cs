using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>Reads one refund of the tenant; another tenant's is not found.</summary>
public sealed class GetRefundHandler(IRefundReadService refunds) : IQueryHandler<GetRefundQuery, RefundDto>
{
    /// <inheritdoc />
    public async Task<Result<RefundDto>> HandleAsync(GetRefundQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var refund = await refunds.GetAsync(query.Id, ct);

        return refund is null ? PaymentsErrors.RefundNotFound() : refund;
    }
}
