using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Payments;

/// <summary>The refunds of the tenant, tracked, for the use cases that change them. The tenant filter of the context applies.</summary>
public sealed class RefundRepository(BillingReconDbContext context) : IRefundRepository
{
    /// <inheritdoc />
    public Task<Refund?> GetAsync(RefundId id, CancellationToken ct)
        => context.Set<Refund>().SingleOrDefaultAsync(refund => refund.Id == id, ct);

    /// <inheritdoc />
    public Task<bool> AnyPostedFromPaymentAsync(PaymentId paymentId, CancellationToken ct)
        => context.Set<Refund>().AnyAsync(refund => refund.SourcePaymentId == paymentId && refund.Status == RefundStatus.Posted, ct);

    /// <inheritdoc />
    public void Add(Refund refund) => context.Set<Refund>().Add(refund);
}
