using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>
/// Part of a payment applied to one posted bill of the same payer (BR-303, BR-304). An allocation belongs to its
/// payment and is created only through <see cref="Payment.Allocate"/>; it has no row version of its own, the
/// payment's covers it. Its amount, bill and posting never change (BR-006); an allocation made in error is reversed,
/// which arrives with reversals (BR-305), never edited or deleted.
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
