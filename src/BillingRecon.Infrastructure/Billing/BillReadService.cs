using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Billing;

/// <summary>Reads bills through the request's context, untracked and filtered by its tenant.</summary>
public sealed class BillReadService(BillingReconDbContext context) : IBillReadService
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

        // The lines are not loaded: a list shows the summary of each bill.
        var totalCount = await bills.CountAsync(ct);
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
