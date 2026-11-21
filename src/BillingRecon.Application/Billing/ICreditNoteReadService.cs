using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>Reads credit notes without tracking them, for queries. Every read is filtered by the current tenant.</summary>
public interface ICreditNoteReadService
{
    /// <summary>The credit note with its lines and applications, or null when the tenant has none with that id.</summary>
    public Task<CreditNoteDto?> GetAsync(CreditNoteId id, CancellationToken ct);

    /// <summary>One page of the tenant's credit notes that match <paramref name="query"/>: the newest date first, then the newest credit note.</summary>
    public Task<PageOf<CreditNoteSummaryDto>> ListAsync(ListCreditNotesQuery query, CancellationToken ct);
}
