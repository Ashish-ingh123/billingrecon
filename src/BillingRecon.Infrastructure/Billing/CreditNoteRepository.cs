using BillingRecon.Application.Billing;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Billing;

/// <summary>
/// Loads credit notes for a change and adds new ones, through the request's context: filtered by its tenant, saved by
/// its unit of work. A credit note is always loaded with its lines and its applications, because the aggregate
/// changes as a whole.
/// </summary>
public sealed class CreditNoteRepository(BillingReconDbContext context) : ICreditNoteRepository
{
    /// <inheritdoc />
    public Task<CreditNote?> GetAsync(CreditNoteId id, CancellationToken ct)
        => context.Set<CreditNote>()
            .Include(creditNote => creditNote.Lines)
            .Include(creditNote => creditNote.Allocations)
            .AsSplitQuery()
            .SingleOrDefaultAsync(creditNote => creditNote.Id == id, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CreditNote>> ListPostedForBillAsync(BillId billId, CancellationToken ct)
        => await context.Set<CreditNote>()
            .AsNoTracking()
            .Include(creditNote => creditNote.Lines)
            .Where(creditNote => creditNote.BillId == billId && creditNote.Status == CreditNoteStatus.Posted)
            .ToListAsync(ct);

    /// <inheritdoc />
    public void Add(CreditNote creditNote) => context.Set<CreditNote>().Add(creditNote);

    /// <inheritdoc />
    public void Add(CreditNoteAllocation allocation) => context.Set<CreditNoteAllocation>().Add(allocation);
}
