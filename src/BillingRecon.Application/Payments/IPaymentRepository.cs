using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>Loads and adds payments for the use cases that change them. No removal: a payment is reversed, never deleted (BR-005).</summary>
public interface IPaymentRepository
{
    /// <summary>The payment, tracked for a change; null when the tenant has no payment with that id.</summary>
    public Task<Payment?> GetAsync(PaymentId id, CancellationToken ct);

    /// <summary>Adds a new payment to the unit of work, which gives it its id.</summary>
    public void Add(Payment payment);
}
