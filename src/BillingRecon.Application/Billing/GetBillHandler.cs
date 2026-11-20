using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>Reads one bill with its lines. Another tenant's bill is not found, exactly like an id nobody has (BR-002).</summary>
public sealed class GetBillHandler(IBillReadService bills) : IQueryHandler<GetBillQuery, BillDto>
{
    /// <inheritdoc />
    public async Task<Result<BillDto>> HandleAsync(GetBillQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var bill = await bills.GetAsync(query.Id, ct);

        return bill is null ? BillingErrors.BillNotFound() : bill;
    }
}
