using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>
/// What Payments answers when an opening batch brings in advances (ADR 0053): which methods of payment there are, which
/// old references the tenant has brought in already, and the opening advance itself, a posted payment of the opening
/// series, added to the unit of work of the batch's approval.
/// </summary>
public interface IOpeningAdvances
{
    /// <summary>The names of the methods of payment, as a file writes them.</summary>
    public IReadOnlyList<string> Methods { get; }

    /// <summary>Those of <paramref name="legacyNumbers"/> that an opening advance of the tenant carries already, as written.</summary>
    public Task<IReadOnlySet<string>> FindBroughtForwardAsync(IReadOnlyCollection<string> legacyNumbers, CancellationToken ct);

    /// <summary>Adds the opening advance <paramref name="advance"/> under <paramref name="number"/> and answers its id, which the ledger posting names.</summary>
    public PaymentId BringForward(OpeningAdvance advance, DocumentNumber number);

    /// <summary>Gives the opening advance just added the ledger posting that belongs to it.</summary>
    public void AttachPosting(PaymentId paymentId, LedgerPostingId postingId);
}
