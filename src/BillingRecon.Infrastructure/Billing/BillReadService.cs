using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Billing;

/// <summary>Reads bills through the request's context, untracked and filtered by its tenant.</summary>
public sealed class BillReadService(BillingReconDbContext context, IPartySearch parties) : IBillReadService
{
    /// <inheritdoc />
    public async Task<BillDto?> GetAsync(BillId id, CancellationToken ct)
    {
        var bill = await Bills().Include(candidate => candidate.Lines).SingleOrDefaultAsync(candidate => candidate.Id == id, ct);

        return bill?.ToDto();
    }

    /// <inheritdoc />
    public async Task<PageOf<BillSummaryDto>> ListAsync(ListBillsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var bills = Bills();

        if (query.PatientId is { } patientId)
        {
            bills = bills.Where(bill => bill.PatientId == patientId);
        }

        if (query.PayerId is { } payerId)
        {
            bills = bills.Where(bill => bill.PayerId == payerId);
        }

        if (query.Status is { } status)
        {
            bills = bills.Where(bill => bill.Status == status);
        }

        if (query.BillDateFrom is { } from)
        {
            bills = bills.Where(bill => bill.BillDate >= from);
        }

        if (query.BillDateTo is { } to)
        {
            bills = bills.Where(bill => bill.BillDate <= to);
        }

        // One box (Prompt 11.1; ADR 0036, amended 2026-11-26). Each part of the search is a query of its own, so that
        // each is answered by its own index, and the documents are those of any part: an OR over them reads every row.
        int? fewFound = null;
        if (DocumentSearchFilter.For(query.Search, parties.PatientsNamed, parties.PayersNamed) is { } search)
        {
            var filtered = bills;
            var found = filtered.Where(bill => EF.Functions.Like(bill.Number!, search.Pattern, LikePattern.Escape)).Select(bill => bill.Id)
                .Union(filtered.Where(bill => EF.Functions.Like(bill.LegacyNumber!, search.Pattern, LikePattern.Escape)).Select(bill => bill.Id))
                .Union(filtered.Where(bill => search.Patients.Contains(bill.PatientId)).Select(bill => bill.Id))
                .Union(filtered.Where(bill => search.Payers.Contains(bill.PayerId)).Select(bill => bill.Id));
            if (search.Amount is { } amount)
            {
                found = found.Union(filtered.Where(bill => bill.Total == amount).Select(bill => bill.Id));
            }

            (bills, fewFound) = await DocumentSearchFilter.NarrowAsync(Bills(), found, filtered.Where(bill => found.Contains(bill.Id)), bill => bill.Id, ct);
        }

        // The lines are not loaded: a list shows the summary of each bill.
        var totalCount = fewFound ?? await bills.CountAsync(ct);
        var page = await bills
            .OrderByDescending(bill => bill.BillDate)
            .ThenByDescending(bill => bill.CreatedAtUtc)
            .ThenBy(bill => bill.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PageOf<BillSummaryDto>([.. page.Select(bill => bill.ToSummary())], query.Page, query.PageSize, totalCount);
    }

    private IQueryable<Bill> Bills() => context.Set<Bill>().AsNoTracking();
}
