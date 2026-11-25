using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// What Billing answers when an opening batch brings in receivables (ADR 0053): which old bill numbers the tenant has
/// brought in already, and the opening receivable itself, a posted bill of the opening series, added to the unit of
/// work of the batch's approval.
/// </summary>
public interface IOpeningReceivables
{
    /// <summary>Those of <paramref name="legacyNumbers"/> that an opening receivable of the tenant carries already, as written.</summary>
    public Task<IReadOnlySet<string>> FindBroughtForwardAsync(IReadOnlyCollection<string> legacyNumbers, CancellationToken ct);

    /// <summary>
    /// Adds the opening receivable <paramref name="receivable"/> under <paramref name="number"/> and answers its id, which
    /// the ledger posting names.
    /// </summary>
    public BillId BringForward(OpeningReceivable receivable, DocumentNumber number, DateTimeOffset postedAtUtc, UserId? postedBy);
}
