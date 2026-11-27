using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Billing;

/// <summary>Reads credit notes through the request's context, untracked and filtered by its tenant.</summary>
public sealed class CreditNoteReadService(BillingReconDbContext context, IPartySearch parties) : ICreditNoteReadService
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

        // The filters of Prompt 12.2 (ADR 0063): each narrows the rows the search and the page are taken from.
        if (query.CreditNoteDateFrom is { } creditNoteDateFrom)
        {
            creditNotes = creditNotes.Where(creditNote => creditNote.CreditNoteDate >= creditNoteDateFrom);
        }

        if (query.CreditNoteDateTo is { } creditNoteDateTo)
        {
            creditNotes = creditNotes.Where(creditNote => creditNote.CreditNoteDate <= creditNoteDateTo);
        }

        if (query.TotalFrom is { } totalFrom)
        {
            creditNotes = creditNotes.Where(creditNote => creditNote.Total >= Money.Of(totalFrom));
        }

        if (query.TotalTo is { } totalTo)
        {
            creditNotes = creditNotes.Where(creditNote => creditNote.Total <= Money.Of(totalTo));
        }

        if (query.CreatedBy is { } createdBy)
        {
            creditNotes = creditNotes.Where(creditNote => creditNote.CreatedByUserId == createdBy);
        }

        // One box (Prompt 11.1; ADR 0036, amended 2026-11-26). Each part of the search is a query of its own, so that
        // each is answered by its own index, and the documents are those of any part: an OR over them reads every row.
        int? fewFound = null;
        if (DocumentSearchFilter.For(query.Search, parties.PatientsNamed, parties.PayersNamed) is { } search)
        {
            var filtered = creditNotes;
            var found = filtered.Where(creditNote => EF.Functions.Like(creditNote.Number!, search.Pattern, LikePattern.Escape)).Select(creditNote => creditNote.Id)
                .Union(filtered.Where(creditNote => search.Patients.Contains(creditNote.PatientId)).Select(creditNote => creditNote.Id))
                .Union(filtered.Where(creditNote => search.Payers.Contains(creditNote.PayerId)).Select(creditNote => creditNote.Id));
            if (search.Amount is { } amount)
            {
                found = found.Union(filtered.Where(creditNote => creditNote.Total == amount).Select(creditNote => creditNote.Id));
            }

            (creditNotes, fewFound) = await DocumentSearchFilter.NarrowAsync(CreditNotes(), found, filtered.Where(creditNote => found.Contains(creditNote.Id)), creditNote => creditNote.Id, ct);
        }

        // The lines and the applications are not loaded: a list shows the summary of each credit note.
        var totalCount = fewFound ?? await creditNotes.CountAsync(ct);
        // The order asked for by name, or the list's own; each ends on a key that is one of a kind (ADR 0063).
        IOrderedQueryable<CreditNote> ordered = query.Order switch
        {
            { Column: "number", Descending: var down } => creditNotes.By(creditNote => creditNote.FiscalYear, down).Then(creditNote => creditNote.SequenceNumber, down).Then(creditNote => creditNote.Id, down),
            { Column: "creditNoteDate", Descending: var down } => creditNotes.By(creditNote => creditNote.CreditNoteDate, down).Then(creditNote => creditNote.Id, down),
            { Column: "total", Descending: var down } => creditNotes.By(creditNote => creditNote.Total, down).Then(creditNote => creditNote.Id, down),
            { Column: "availableAmount", Descending: var down } => creditNotes.By(creditNote => creditNote.AvailableAmount, down).Then(creditNote => creditNote.Id, down),
            null => creditNotes
                .OrderByDescending(creditNote => creditNote.CreditNoteDate)
                .ThenByDescending(creditNote => creditNote.CreatedAtUtc)
                .ThenBy(creditNote => creditNote.Id),
            _ => throw new InvalidOperationException($"The order {query.Order} is not one of this list; its validator should have refused it."),
        };
        var page = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PageOf<CreditNoteSummaryDto>([.. page.Select(creditNote => creditNote.ToSummary())], query.Page, query.PageSize, totalCount);
    }

    private IQueryable<CreditNote> CreditNotes() => context.Set<CreditNote>().AsNoTracking();
}
