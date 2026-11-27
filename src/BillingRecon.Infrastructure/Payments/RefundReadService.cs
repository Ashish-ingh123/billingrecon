using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Payments;

/// <summary>Reads refunds for the API, without tracking. The tenant filter of the context applies.</summary>
public sealed class RefundReadService(BillingReconDbContext context, IPartySearch parties) : IRefundReadService
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

        // The filters of Prompt 12.2 (ADR 0063): each narrows the rows the search and the page are taken from.
        if (query.RefundDateFrom is { } refundDateFrom)
        {
            refunds = refunds.Where(refund => refund.RefundDate >= refundDateFrom);
        }

        if (query.RefundDateTo is { } refundDateTo)
        {
            refunds = refunds.Where(refund => refund.RefundDate <= refundDateTo);
        }

        if (query.Method is { } method)
        {
            refunds = refunds.Where(refund => refund.Method == method);
        }

        if (query.AmountFrom is { } amountFrom)
        {
            refunds = refunds.Where(refund => refund.Amount >= Money.Of(amountFrom));
        }

        if (query.AmountTo is { } amountTo)
        {
            refunds = refunds.Where(refund => refund.Amount <= Money.Of(amountTo));
        }

        if (query.CreatedBy is { } createdBy)
        {
            refunds = refunds.Where(refund => refund.CreatedByUserId == createdBy);
        }

        // One box (Prompt 11.1; ADR 0036, amended 2026-11-26). Each part of the search is a query of its own, so that
        // each is answered by its own index, and the documents are those of any part: an OR over them reads every row.
        int? fewFound = null;
        if (DocumentSearchFilter.For(query.Search, parties.PatientsNamed, parties.PayersNamed) is { } search)
        {
            var filtered = refunds;
            var found = filtered.Where(refund => EF.Functions.Like(refund.Number!, search.Pattern, LikePattern.Escape)).Select(refund => refund.Id)
                .Union(filtered.Where(refund => search.Payers.Contains(refund.PayerId)).Select(refund => refund.Id));
            if (search.Amount is { } amount)
            {
                found = found.Union(filtered.Where(refund => refund.Amount == amount).Select(refund => refund.Id));
            }

            (refunds, fewFound) = await DocumentSearchFilter.NarrowAsync(Refunds(), found, filtered.Where(refund => found.Contains(refund.Id)), refund => refund.Id, ct);
        }

        var totalCount = fewFound ?? await refunds.CountAsync(ct);
        // The order asked for by name, or the list's own; each ends on a key that is one of a kind (ADR 0063).
        IOrderedQueryable<Refund> ordered = query.Order switch
        {
            { Column: "number", Descending: var down } => refunds.By(refund => refund.FiscalYear, down).Then(refund => refund.SequenceNumber, down).Then(refund => refund.Id, down),
            { Column: "refundDate", Descending: var down } => refunds.By(refund => refund.RefundDate, down).Then(refund => refund.Id, down),
            { Column: "amount", Descending: var down } => refunds.By(refund => refund.Amount, down).Then(refund => refund.Id, down),
            null => refunds
                .OrderByDescending(refund => refund.RefundDate)
                .ThenByDescending(refund => refund.CreatedAtUtc)
                .ThenBy(refund => refund.Id),
            _ => throw new InvalidOperationException($"The order {query.Order} is not one of this list; its validator should have refused it."),
        };
        var page = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PageOf<RefundDto>([.. page.Select(refund => refund.ToDto())], query.Page, query.PageSize, totalCount);
    }

    private IQueryable<Refund> Refunds() => context.Set<Refund>().AsNoTracking();
}
