using BillingRecon.Application.Common;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Payments;

/// <summary>Reads refunds for the API, without tracking. The tenant filter of the context applies.</summary>
public sealed class RefundReadService(BillingReconDbContext context) : IRefundReadService
{
    /// <inheritdoc />
    public Task<UserId?> FindRecorderAsync(RefundId id, CancellationToken ct)
        => context.Set<Refund>().Where(refund => refund.Id == id).Select(refund => refund.CreatedByUserId).SingleOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<RefundDto?> GetAsync(RefundId id, CancellationToken ct)
    {
        var refund = await Refunds().SingleOrDefaultAsync(candidate => candidate.Id == id, ct);

        return refund?.ToDto();
    }

    /// <inheritdoc />
    public async Task<PageOf<RefundDto>> ListAsync(ListRefundsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var refunds = Refunds();
        if (query.PayerId is { } payerId)
        {
            refunds = refunds.Where(refund => refund.PayerId == payerId);
        }

        if (query.Status is { } status)
        {
            refunds = refunds.Where(refund => refund.Status == status);
        }

        if (query.PaymentId is { } paymentId)
        {
            refunds = refunds.Where(refund => refund.SourcePaymentId == paymentId);
        }

        if (query.CreditNoteId is { } creditNoteId)
        {
            refunds = refunds.Where(refund => refund.SourceCreditNoteId == creditNoteId);
        }

        var totalCount = await refunds.CountAsync(ct);
        var page = await refunds
            .OrderByDescending(refund => refund.RefundDate)
            .ThenByDescending(refund => refund.CreatedAtUtc)
            .ThenBy(refund => refund.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PageOf<RefundDto>([.. page.Select(refund => refund.ToDto())], query.Page, query.PageSize, totalCount);
    }

    private IQueryable<Refund> Refunds() => context.Set<Refund>().AsNoTracking();
}
