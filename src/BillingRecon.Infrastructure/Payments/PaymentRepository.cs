using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Payments;

/// <summary>Loads and adds payments through the context of the request. Reads are filtered by the current tenant.</summary>
public sealed class PaymentRepository(BillingReconDbContext context) : IPaymentRepository
{
    /// <inheritdoc />
    public Task<Payment?> GetAsync(PaymentId id, CancellationToken ct)
        => context.Set<Payment>().SingleOrDefaultAsync(payment => payment.Id == id, ct);

    /// <inheritdoc />
    public void Add(Payment payment) => context.Set<Payment>().Add(payment);
}
