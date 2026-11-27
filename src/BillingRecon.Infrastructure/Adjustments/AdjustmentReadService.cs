using BillingRecon.Application.Adjustments;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Adjustments;

/// <summary>Reads adjustments through the request's context, untracked and filtered by its tenant.</summary>
public sealed class AdjustmentReadService(BillingReconDbContext context, IPartySearch parties) : IAdjustmentReadService
{
    /// <inheritdoc />
    public async Task<AdjustmentDto?> GetAsync(AdjustmentId id, CancellationToken ct)
    {
        var adjustment = await Adjustments().SingleOrDefaultAsync(candidate => candidate.Id == id, ct);

        return adjustment?.ToDto();
    }

    /// <inheritdoc />
    public async Task<PageOf<AdjustmentDto>> ListAsync(ListAdjustmentsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var adjustments = Adjustments();
        if (query.BillId is { } billId)
        {
            adjustments = adjustments.Where(adjustment => adjustment.BillId == billId);
        }

        if (query.PayerId is { } payerId)
        {
            adjustments = adjustments.Where(adjustment => adjustment.PayerId == payerId);
        }

        if (query.Status is { } status)
        {
            adjustments = adjustments.Where(adjustment => adjustment.Status == status);
        }

        if (query.Kind is { } kind)
        {
            adjustments = adjustments.Where(adjustment => adjustment.Kind == kind);
        }

        // The filters of Prompt 12.2 (ADR 0063): each narrows the rows the search and the page are taken from.
        if (query.AdjustmentDateFrom is { } adjustmentDateFrom)
        {
            adjustments = adjustments.Where(adjustment => adjustment.AdjustmentDate >= adjustmentDateFrom);
        }

        if (query.AdjustmentDateTo is { } adjustmentDateTo)
        {
            adjustments = adjustments.Where(adjustment => adjustment.AdjustmentDate <= adjustmentDateTo);
        }

        if (query.AmountFrom is { } amountFrom)
        {
            adjustments = adjustments.Where(adjustment => adjustment.Amount >= Money.Of(amountFrom));
        }

        if (query.AmountTo is { } amountTo)
        {
            adjustments = adjustments.Where(adjustment => adjustment.Amount <= Money.Of(amountTo));
        }

        if (query.CreatedBy is { } createdBy)
        {
            adjustments = adjustments.Where(adjustment => adjustment.CreatedByUserId == createdBy);
        }

        // One box (Prompt 11.1; ADR 0036, amended 2026-11-26). Each part of the search is a query of its own, so that
        // each is answered by its own index, and the documents are those of any part: an OR over them reads every row.
        int? fewFound = null;
        if (DocumentSearchFilter.For(query.Search, parties.PatientsNamed, parties.PayersNamed) is { } search)
        {
            var filtered = adjustments;
            var found = filtered.Where(adjustment => EF.Functions.Like(adjustment.Number!, search.Pattern, LikePattern.Escape)).Select(adjustment => adjustment.Id)
                .Union(filtered.Where(adjustment => search.Payers.Contains(adjustment.PayerId)).Select(adjustment => adjustment.Id));
            if (search.Amount is { } amount)
            {
                found = found.Union(filtered.Where(adjustment => adjustment.Amount == amount).Select(adjustment => adjustment.Id));
            }

            (adjustments, fewFound) = await DocumentSearchFilter.NarrowAsync(Adjustments(), found, filtered.Where(adjustment => found.Contains(adjustment.Id)), adjustment => adjustment.Id, ct);
        }

        var totalCount = fewFound ?? await adjustments.CountAsync(ct);
        // The order asked for by name, or the list's own; each ends on a key that is one of a kind (ADR 0063).
        IOrderedQueryable<Adjustment> ordered = query.Order switch
        {
            { Column: "number", Descending: var down } => adjustments.By(adjustment => adjustment.FiscalYear, down).Then(adjustment => adjustment.SequenceNumber, down).Then(adjustment => adjustment.Id, down),
            { Column: "adjustmentDate", Descending: var down } => adjustments.By(adjustment => adjustment.AdjustmentDate, down).Then(adjustment => adjustment.Id, down),
            { Column: "amount", Descending: var down } => adjustments.By(adjustment => adjustment.Amount, down).Then(adjustment => adjustment.Id, down),
            null => adjustments
                .OrderByDescending(adjustment => adjustment.AdjustmentDate)
                .ThenByDescending(adjustment => adjustment.CreatedAtUtc)
                .ThenBy(adjustment => adjustment.Id),
            _ => throw new InvalidOperationException($"The order {query.Order} is not one of this list; its validator should have refused it."),
        };
        var page = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PageOf<AdjustmentDto>([.. page.Select(adjustment => adjustment.ToDto())], query.Page, query.PageSize, totalCount);
    }

    private IQueryable<Adjustment> Adjustments() => context.Set<Adjustment>().AsNoTracking();
}
