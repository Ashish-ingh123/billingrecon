using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>Reads refunds for the API, without tracking.</summary>
public interface IRefundReadService
{
    /// <summary>The refund with that id; null when the tenant has none.</summary>
    public Task<RefundDto?> GetAsync(RefundId id, CancellationToken ct);

    /// <summary>A page of the refunds that match the query, the latest refund date first.</summary>
    public Task<PageOf<RefundDto>> ListAsync(ListRefundsQuery query, CancellationToken ct);
}
