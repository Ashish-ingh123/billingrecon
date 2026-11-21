using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Loads and adds credit notes for the commands that change them. A credit note is loaded whole, with its lines and
/// its applications. Reads are filtered by the current tenant. There is no remove: a draft is voided, never deleted
/// (BR-005).
/// </summary>
public interface ICreditNoteRepository
{
    /// <summary>The credit note with its lines and applications, tracked for a change, or null when the tenant has none with that id.</summary>
    public Task<CreditNote?> GetAsync(CreditNoteId id, CancellationToken ct);

    /// <summary>
    /// The posted credit notes of a bill with their lines, not tracked: what the caps of BR-210 and BR-211 are
    /// measured against. Read from the database each time it is asked, so a second call inside a transaction sees
    /// what was committed since the first.
    /// </summary>
    public Task<IReadOnlyList<CreditNote>> ListPostedForBillAsync(BillId billId, CancellationToken ct);

    /// <summary>Adds a new credit note to the unit of work; it is saved by the commit.</summary>
    public void Add(CreditNote creditNote);

    /// <summary>
    /// Adds an application its credit note has just made to the unit of work, which gives it its id before the save:
    /// the ledger posting of the application names it.
    /// </summary>
    public void Add(CreditNoteAllocation allocation);
}
