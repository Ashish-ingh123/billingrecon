using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>Reads payments without tracking them, filtered by the current tenant.</summary>
public interface IPaymentReadService
{
    /// <summary>The payment, or null when the tenant has no payment with that id.</summary>
    public Task<PaymentDto?> GetAsync(PaymentId id, CancellationToken ct);

    /// <summary>The payment with every allocation made from it, the oldest first; null when the tenant has no payment with that id.</summary>
    public Task<PaymentAllocationsDto?> GetWithAllocationsAsync(PaymentId id, CancellationToken ct);

    /// <summary>The user who recorded the payment; null when the tenant has no such payment or no user recorded it.</summary>
    public Task<UserId?> FindRecorderAsync(PaymentId id, CancellationToken ct);

    /// <summary>One page of the payments that match <paramref name="query"/>, the newest payment date first.</summary>
    public Task<PageOf<PaymentDto>> ListAsync(ListPaymentsQuery query, CancellationToken ct);
}
