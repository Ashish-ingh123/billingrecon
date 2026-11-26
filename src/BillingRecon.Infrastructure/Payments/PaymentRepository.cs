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
        => context.Set<Payment>().Include(payment => payment.Allocations).SingleOrDefaultAsync(payment => payment.Id == id, ct);

    /// <inheritdoc />
    /// <inheritdoc />
    public Task<string?> FindNumberByReferenceAsync(PayerId payerId, PaymentMethod method, string reference, CancellationToken ct)
        => context.Set<Payment>()
            .AsNoTracking()
            .Where(payment => payment.PayerId == payerId && payment.Method == method && payment.Reference == reference && payment.Status != PaymentStatus.Reversed)
            .Select(payment => (string?)payment.Number)
            .FirstOrDefaultAsync(ct);

    public void Add(Payment payment) => context.Set<Payment>().Add(payment);

    /// <inheritdoc />
    public void Add(PaymentAllocation allocation) => context.Set<PaymentAllocation>().Add(allocation);
}
