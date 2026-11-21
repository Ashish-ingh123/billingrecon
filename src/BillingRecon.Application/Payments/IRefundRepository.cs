using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>The refunds of the tenant, for the use cases that change them.</summary>
public interface IRefundRepository
{
    /// <summary>The refund with that id, tracked; null when the tenant has none.</summary>
    public Task<Refund?> GetAsync(RefundId id, CancellationToken ct);

    /// <summary>Whether a posted refund pays from payment <paramref name="paymentId"/>, which then cannot be reversed (BR-306).</summary>
    public Task<bool> AnyPostedFromPaymentAsync(PaymentId paymentId, CancellationToken ct);

    /// <summary>Adds a new refund to the unit of work, which gives it its id.</summary>
    public void Add(Refund refund);
}
