using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// Part of a posted credit note applied to one posted bill of the same payer (BR-212, BR-213): the bill the credit
/// note was written against, at posting, or another bill later. It belongs to its credit note and is created only
/// through <see cref="CreditNote.Apply"/>; it has no row version of its own, the credit note's covers it. Its amount,
/// bill and posting never change (BR-006), and it is never deleted.
/// </summary>
public sealed class CreditNoteAllocation : Entity<CreditNoteAllocationId>, ITenantScoped
{
    // Entity Framework creates an application with this constructor and then sets the properties.
    private CreditNoteAllocation()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The credit note the amount comes from. Set when the application is saved.</summary>
    public CreditNoteId CreditNoteId { get; private set; }

    /// <summary>The bill the amount is applied to.</summary>
    public BillId BillId { get; private set; }

    /// <summary>The amount applied, above zero.</summary>
    public Money Amount { get; private set; }

    /// <summary>Where the application is in its life.</summary>
    public CreditNoteAllocationStatus Status { get; private set; }

    /// <summary>When the amount was applied.</summary>
    public DateTimeOffset AllocatedAtUtc { get; private set; }

    /// <summary>Who applied it; null when no user did.</summary>
    public UserId? AllocatedByUserId { get; private set; }

    /// <summary>
    /// The ledger posting of the application: Dr CreditAvailable(credit note), Cr Receivable(bill) (BR-212). Set by
    /// the use case before it saves.
    /// </summary>
    public LedgerPostingId PostingId { get; private set; }

    internal static CreditNoteAllocation Create(BillId billId, Money amount, DateTimeOffset allocatedAtUtc, UserId? allocatedBy) => new()
    {
        BillId = billId,
        Amount = amount,
        Status = CreditNoteAllocationStatus.Active,
        AllocatedAtUtc = allocatedAtUtc,
        AllocatedByUserId = allocatedBy,
    };

    internal void AttachPosting(LedgerPostingId postingId)
    {
        if (postingId.Value == Guid.Empty)
        {
            throw new ArgumentException("An application belongs to a ledger posting; the empty id is not one.", nameof(postingId));
        }

        if (PostingId.Value != Guid.Empty)
        {
            throw new InvalidOperationException("The application has its ledger posting already; a posting is never replaced (BR-006).");
        }

        PostingId = postingId;
    }
}
