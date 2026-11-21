using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>
/// Part of a payment applied to one posted bill of the same payer (BR-303, BR-304). An allocation belongs to its
/// payment and is created only through <see cref="Payment.Allocate"/>; it has no row version of its own, the
/// payment's covers it. Its amount, bill and posting never change (BR-006); an allocation made in error is reversed
/// (BR-305), through <see cref="Payment.RequestAllocationReversal"/> and a second person's approval, never edited or
/// deleted: the row stays, marked <see cref="PaymentAllocationStatus.Reversed"/>, with who, when, why and the mirror
/// posting.
/// </summary>
public sealed class PaymentAllocation : Entity<PaymentAllocationId>, ITenantScoped
{
    // Entity Framework creates an allocation with this constructor and then sets the properties.
    private PaymentAllocation()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The payment the amount comes from. Set when the allocation is saved.</summary>
    public PaymentId PaymentId { get; private set; }

    /// <summary>The bill the amount is applied to.</summary>
    public BillId BillId { get; private set; }

    /// <summary>The amount applied, above zero.</summary>
    public Money Amount { get; private set; }

    /// <summary>Where the allocation is in its life.</summary>
    public PaymentAllocationStatus Status { get; private set; }

    /// <summary>When the allocation was made.</summary>
    public DateTimeOffset AllocatedAtUtc { get; private set; }

    /// <summary>Who made the allocation; null when no user did.</summary>
    public UserId? AllocatedByUserId { get; private set; }

    /// <summary>
    /// The ledger posting of the allocation: Dr UnappliedCash(payment), Cr Receivable(bill) (BR-303). Set by the use
    /// case before it saves.
    /// </summary>
    public LedgerPostingId PostingId { get; private set; }

    /// <summary>When the allocation was reversed; null unless it was.</summary>
    public DateTimeOffset? ReversedAtUtc { get; private set; }

    /// <summary>Who approved the reversal, the checker; null unless the allocation was reversed.</summary>
    public UserId? ReversedByUserId { get; private set; }

    /// <summary>Why the allocation was reversed, in the words of the cashier who asked; null unless it was.</summary>
    public string? ReversalReason { get; private set; }

    /// <summary>
    /// The mirror ledger posting of the reversal: Dr Receivable(bill), Cr UnappliedCash(payment) (BR-305); null unless
    /// the allocation was reversed. Set by the use case before it saves.
    /// </summary>
    public LedgerPostingId? ReversalPostingId { get; private set; }

    /// <summary>Whether the amount is applied to the bill: the allocation is in force, or waits for a decision on its reversal.</summary>
    public bool IsApplied => Status is PaymentAllocationStatus.Active or PaymentAllocationStatus.PendingReversal;

    internal void MarkPendingReversal() => Status = PaymentAllocationStatus.PendingReversal;

    internal void MarkActive() => Status = PaymentAllocationStatus.Active;

    internal void MarkReversed(string reason, DateTimeOffset reversedAtUtc, UserId reversedBy)
    {
        Status = PaymentAllocationStatus.Reversed;
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

        if (Status != PaymentAllocationStatus.Reversed || ReversalPostingId is not null)
        {
            throw new InvalidOperationException("Only a reversed allocation takes its reversal posting, and only once (BR-006).");
        }

        ReversalPostingId = postingId;
    }

    internal static PaymentAllocation Create(BillId billId, Money amount, DateTimeOffset allocatedAtUtc, UserId? allocatedBy) => new()
    {
        BillId = billId,
        Amount = amount,
        Status = PaymentAllocationStatus.Active,
        AllocatedAtUtc = allocatedAtUtc,
        AllocatedByUserId = allocatedBy,
    };

    internal void AttachPosting(LedgerPostingId postingId)
    {
        if (postingId.Value == Guid.Empty)
        {
            throw new ArgumentException("An allocation belongs to a ledger posting; the empty id is not one.", nameof(postingId));
        }

        if (PostingId.Value != Guid.Empty)
        {
            throw new InvalidOperationException("The allocation has its ledger posting already; a posting is never replaced (BR-006).");
        }

        PostingId = postingId;
    }
}
