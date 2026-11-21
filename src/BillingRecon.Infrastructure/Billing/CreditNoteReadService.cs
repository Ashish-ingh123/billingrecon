using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Billing;

/// <summary>Reads credit notes through the request's context, untracked and filtered by its tenant.</summary>
public sealed class CreditNoteReadService(BillingReconDbContext context) : ICreditNoteReadService
{
    /// <inheritdoc />
    public async Task<CreditNoteDto?> GetAsync(CreditNoteId id, CancellationToken ct)
    {
        var creditNote = await CreditNotes()
            .Include(candidate => candidate.Lines)
            .Include(candidate => candidate.Allocations)
            .AsSplitQuery()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, ct);

        return creditNote?.ToDto();
    }

    /// <inheritdoc />
    public async Task<PageOf<CreditNoteSummaryDto>> ListAsync(ListCreditNotesQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var creditNotes = CreditNotes();
        if (query.BillId is { } billId)
        {
            creditNotes = creditNotes.Where(creditNote => creditNote.BillId == billId);
        }

        if (query.PayerId is { } payerId)
        {
            creditNotes = creditNotes.Where(creditNote => creditNote.PayerId == payerId);
        }

        if (query.Status is { } status)
        {
            creditNotes = creditNotes.Where(creditNote => creditNote.Status == status);
        }

        // The lines and the applications are not loaded: a list shows the summary of each credit note.
        var totalCount = await creditNotes.CountAsync(ct);
        var page = await creditNotes
            .OrderByDescending(creditNote => creditNote.CreditNoteDate)
            .ThenByDescending(creditNote => creditNote.CreatedAtUtc)
            .ThenBy(creditNote => creditNote.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PageOf<CreditNoteSummaryDto>([.. page.Select(creditNote => creditNote.ToSummary())], query.Page, query.PageSize, totalCount);
    }

    private IQueryable<CreditNote> CreditNotes() => context.Set<CreditNote>().AsNoTracking();
}
