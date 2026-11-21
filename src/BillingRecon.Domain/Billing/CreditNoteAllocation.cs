using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// Part of a posted credit note applied to one posted bill of the same payer (BR-212, BR-213): the bill the credit
/// note was written against, at posting, or another bill later. It belongs to its credit note and is created only
/// through the credit note (<see cref="CreditNote.ApplyToOwnBillAtPosting"/> and <see cref="CreditNote.Apply"/>); it
/// has no row version of its own, the credit note's covers it. Its amount, bill and posting never change (BR-006),
/// and it is never deleted.
/// <para>
/// An application made later, through a request of its own, can be reversed (ADR 0028), through
/// <see cref="CreditNote.RequestApplicationReversal"/> and a second person's approval: the row stays, marked
/// <see cref="CreditNoteAllocationStatus.Reversed"/>, with who, when, why and the mirror posting. The application a
/// credit note makes to its own bill when it is posted is part of what the credit note is, and is never reversed.
/// </para>
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
    /// Whether this is the application the credit note made to its own bill when it was posted (BR-212). Such an
    /// application is never reversed (ADR 0028): it is what the credit note corrects.
    /// </summary>
    public bool AppliedAtPosting { get; private set; }

    /// <summary>
    /// The ledger posting of the application: Dr CreditAvailable(credit note), Cr Receivable(bill) (BR-212). Set by
    /// the use case before it saves.
    /// </summary>
    public LedgerPostingId PostingId { get; private set; }

    /// <summary>When the application was reversed; null unless it was.</summary>
    public DateTimeOffset? ReversedAtUtc { get; private set; }

    /// <summary>Who approved the reversal, the checker; null unless the application was reversed.</summary>
    public UserId? ReversedByUserId { get; private set; }

    /// <summary>Why the application was reversed, in the words of the cashier who asked; null unless it was.</summary>
    public string? ReversalReason { get; private set; }

    /// <summary>
    /// The mirror ledger posting of the reversal: Dr Receivable(bill), Cr CreditAvailable(credit note) (ADR 0028);
    /// null unless the application was reversed. Set by the use case before it saves.
    /// </summary>
    public LedgerPostingId? ReversalPostingId { get; private set; }

    /// <summary>Whether the amount is applied to the bill: the application is in force, or waits for a decision on its reversal.</summary>
    public bool IsApplied => Status is CreditNoteAllocationStatus.Active or CreditNoteAllocationStatus.PendingReversal;

    internal static CreditNoteAllocation Create(BillId billId, Money amount, DateTimeOffset allocatedAtUtc, UserId? allocatedBy, bool appliedAtPosting) => new()
    {
        BillId = billId,
        Amount = amount,
        Status = CreditNoteAllocationStatus.Active,
        AllocatedAtUtc = allocatedAtUtc,
        AllocatedByUserId = allocatedBy,
        AppliedAtPosting = appliedAtPosting,
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

    internal void MarkPendingReversal() => Status = CreditNoteAllocationStatus.PendingReversal;

    internal void MarkActive() => Status = CreditNoteAllocationStatus.Active;

    internal void MarkReversed(string reason, DateTimeOffset reversedAtUtc, UserId reversedBy)
    {
        Status = CreditNoteAllocationStatus.Reversed;
        ReversedAtUtc = reversedAtUtc;
        ReversedByUserId = reversedBy;
        ReversalReason = reason;
    }

    internal void AttachReversalPosting(LedgerPostingId postingId)
    {
        if (postingId.Value == Guid.Empty)
        {
            throw new ArgumentException("A reversal belongs to a ledger posting; the empty id is not one.", nameof(postingId));
        }

        if (Status != CreditNoteAllocationStatus.Reversed || ReversalPostingId is not null)
        {
            throw new InvalidOperationException("Only a reversed application takes its reversal posting, and only once (BR-006).");
        }

        ReversalPostingId = postingId;
    }
}
