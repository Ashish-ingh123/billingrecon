using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>
/// Money received from a payer (BR-301, BR-302, BR-309; docs/domain-model.md, section 4.6). A payment has no draft:
/// recording it posts it, so it is created with its gapless number, and its amount, date, method, reference and
/// payer never change afterwards (BR-006). What is not yet applied to a bill is <see cref="UnappliedAmount"/>, the
/// cache of the payment's unapplied cash in the ledger (BR-405); on a new payment that is the whole amount.
/// <para>
/// Whether the payer exists and is active is the use case's question (BR-309): it needs another aggregate. The date
/// follows BR-205, with today and the tenant's back-dating limit handed in as a <see cref="BusinessDateWindow"/>.
/// </para>
/// <para>
/// A payment is applied to posted bills of its payer through <see cref="Allocate"/> (BR-303, BR-304): each
/// allocation lowers <see cref="UnappliedAmount"/> and is kept as a <see cref="PaymentAllocation"/>. Whether the bill
/// can take the amount is the bill's question, which the use case asks through the Billing module's contract.
/// </para>
/// <para>
/// An allocation, or the whole payment, is taken back only by a reversal (BR-305, BR-306), which a cashier asks for
/// and a second person approves: the rows stay, marked reversed, and the ledger gets the mirror postings. While the
/// payment waits for that decision nothing can be allocated from it; once reversed it never changes again. Refunds
/// arrive with their slice.
/// </para>
/// </summary>
public sealed class Payment : AggregateRoot<PaymentId>, ITenantScoped, ICreationAudited, IModificationAudited, IHasRowVersion
{
    /// <summary>Longest allowed <see cref="Reference"/>.</summary>
    public const int ReferenceMaxLength = 100;

    /// <summary>Longest allowed <see cref="Notes"/>.</summary>
    public const int NotesMaxLength = 500;

    /// <summary>The largest amount one payment may have: sums of payments stay far inside what a <c>decimal(18,2)</c> column holds.</summary>
    public const decimal MaxAmount = 9_999_999_999.99m;

    /// <summary>Longest allowed reason of a reversal, of the payment or of one of its allocations.</summary>
    public const int ReversalReasonMaxLength = 500;

    private readonly List<PaymentAllocation> _allocations = [];

    // Entity Framework creates a payment with this constructor and then sets the properties.
    private Payment()
    {
        Number = string.Empty;
        RowVersion = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The gapless number of the payment, as it is printed on the receipt slip (BR-801).</summary>
    public string Number { get; private set; }

    /// <summary>The fiscal year of the payment date, which the number belongs to (BR-004).</summary>
    public FiscalYear FiscalYear { get; private set; }

    /// <summary>The position of the payment in the series of its fiscal year, from 1 (BR-801).</summary>
    public int SequenceNumber { get; private set; }

    /// <summary>The payer the money came from. It never changes.</summary>
    public PayerId PayerId { get; private set; }

    /// <summary>The business date the money was received, a calendar date in India (BR-003, BR-205).</summary>
    public DateOnly PaymentDate { get; private set; }

    /// <summary>How the money was received (BR-301).</summary>
    public PaymentMethod Method { get; private set; }

    /// <summary>The UPI id, card authorisation, UTR or cheque number; required unless the method is cash (BR-301).</summary>
    public string? Reference { get; private set; }

    /// <summary>The money received, above zero.</summary>
    public Money Amount { get; private set; }

    /// <summary>What of the amount is not yet applied to a bill or refunded (BR-405). The whole amount on a new payment.</summary>
    public Money UnappliedAmount { get; private set; }

    /// <summary>Where the payment is in its life.</summary>
    public PaymentStatus Status { get; private set; }

    /// <summary>A note of the cashier.</summary>
    public string? Notes { get; private set; }

    /// <summary>The ledger posting that recorded the payment: Dr Cash, Cr UnappliedCash (BR-302). Set by the use case before it saves.</summary>
    public LedgerPostingId PostingId { get; private set; }

    /// <summary>When the payment was reversed; null unless it was.</summary>
    public DateTimeOffset? ReversedAtUtc { get; private set; }

    /// <summary>Who approved the reversal, the checker; null unless the payment was reversed.</summary>
    public UserId? ReversedByUserId { get; private set; }

    /// <summary>Why the payment was reversed, in the words of the cashier who asked; null unless it was.</summary>
    public string? ReversalReason { get; private set; }

    /// <summary>
    /// The ledger posting of the reversal: Dr UnappliedCash(payment), Cr Cash (BR-306); null unless the payment was
    /// reversed. Set by the use case before it saves.
    /// </summary>
    public LedgerPostingId? ReversalPostingId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? CreatedByUserId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? UpdatedByUserId { get; private set; }

    /// <inheritdoc />
    public byte[] RowVersion { get; private set; }

    /// <summary>The allocations of the payment to bills, in no promised order.</summary>
    public IReadOnlyList<PaymentAllocation> Allocations => _allocations;

    /// <summary>
    /// Whether a payment dated <paramref name="paymentDate"/> may be recorded today (BR-205). The use case asks before
    /// it takes the number, so that a refusal never touches the series (BR-801).
    /// </summary>
    public static Result CheckDate(DateOnly paymentDate, BusinessDateWindow window)
    {
        if (paymentDate > window.Latest)
        {
            return PaymentsErrors.PaymentDateInFuture();
        }

        return paymentDate < window.Earliest ? PaymentsErrors.PaymentDateTooOld(window.Earliest) : Result.Success();
    }

    /// <summary>
    /// Records, and thereby posts, a payment under <paramref name="number"/> (BR-302). The date must lie in
    /// <paramref name="window"/>; a date outside it is returned as a failure and nothing is created.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The payer's id is empty; the amount is not above zero or above <see cref="MaxAmount"/>; the method is not one
    /// the enum defines; a method other than cash has no reference; a text is too long; or the number is not a payment
    /// number of the fiscal year of the payment date. The validator of the use case refuses all of these first.
    /// </exception>
    public static Result<Payment> Record(
        PayerId payerId,
        DateOnly paymentDate,
        PaymentMethod method,
        string? reference,
        Money amount,
        string? notes,
        DocumentNumber number,
        BusinessDateWindow window)
    {
        ArgumentNullException.ThrowIfNull(number);

        if (payerId.Value == Guid.Empty)
        {
            throw new ArgumentException("A payment comes from a payer; the empty id is not one.", nameof(payerId));
        }

        if (!Enum.IsDefined(method))
        {
            throw new ArgumentOutOfRangeException(nameof(method), method, "The payment method is not one the enum defines.");
        }

        if (!amount.IsPositive || amount.Amount > MaxAmount)
        {
            throw new ArgumentException($"A payment has an amount above zero and at most {MaxAmount}; the validator of the use case should have refused it.", nameof(amount));
        }

        var trimmedReference = Field.Optional(reference, ReferenceMaxLength);
        if (method != PaymentMethod.Cash && trimmedReference is null)
        {
            throw new ArgumentException("A payment that is not cash has a reference (BR-301); the validator of the use case should have refused it.", nameof(reference));
        }

        var trimmedNotes = Field.Optional(notes, NotesMaxLength);

        var dated = CheckDate(paymentDate, window);
        if (dated.IsFailure)
        {
            return dated.Error;
        }

        if (number.Type != DocumentType.Payment || number.FiscalYear != Common.FiscalYear.FromDate(paymentDate))
        {
            throw new ArgumentException("A payment is recorded under a payment number of the fiscal year of its payment date (BR-302).", nameof(number));
        }

        var payment = new Payment
        {
            Number = number.Value,
            FiscalYear = number.FiscalYear,
            SequenceNumber = number.Sequence,
            PayerId = payerId,
            PaymentDate = paymentDate,
            Method = method,
            Reference = trimmedReference,
            Amount = amount,
            UnappliedAmount = amount,
            Status = PaymentStatus.Posted,
            Notes = trimmedNotes,
        };

        // The id is still empty here: it is assigned when the payment is added to the unit of work.
        payment.AddDomainEvent(new PaymentRecorded(number.Value, payerId, method, amount, paymentDate));

        return payment;
    }

    /// <summary>
    /// Records which ledger posting belongs to the payment. The use case calls it once, between the ledger and the
    /// save: the posting needs the id of the payment, which exists only after the payment joined the unit of work.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="postingId"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">The payment has its posting already.</exception>
    public void AttachPosting(LedgerPostingId postingId)
    {
        if (postingId.Value == Guid.Empty)
        {
            throw new ArgumentException("A payment belongs to a ledger posting; the empty id is not one.", nameof(postingId));
        }

        if (PostingId.Value != Guid.Empty)
        {
            throw new InvalidOperationException("The payment has its ledger posting already; a posting is never replaced (BR-006).");
        }

        PostingId = postingId;
    }

    /// <summary>
    /// Whether <paramref name="amount"/> can still be allocated from the payment (BR-303): it is not more than what
    /// is unapplied. The use case asks for the sum of a request before it changes anything.
    /// </summary>
    public Result CheckCanAllocate(Money amount)
    {
        if (RefuseUnlessPosted() is { } refused)
        {
            return refused;
        }

        return amount > UnappliedAmount ? PaymentsErrors.AllocationExceedsUnapplied(Number, UnappliedAmount) : Result.Success();
    }

    /// <summary>
    /// Applies <paramref name="amount"/> of the payment to bill <paramref name="billId"/> (BR-303): the unapplied
    /// amount falls by it and the allocation is kept. More than what is unapplied is refused and changes nothing.
    /// That the bill is posted, belongs to the same payer and is owed at least the amount is checked by the bill,
    /// in the same use case.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The bill's id is empty or the amount is not above zero; the validator of the use case refuses both first.
    /// </exception>
    public Result<PaymentAllocation> Allocate(BillId billId, Money amount, DateTimeOffset allocatedAtUtc, UserId? allocatedBy)
    {
        if (billId.Value == Guid.Empty)
        {
            throw new ArgumentException("A payment is allocated to a bill; the empty id is not one.", nameof(billId));
        }

        if (!amount.IsPositive)
        {
            throw new ArgumentException("An allocation has an amount above zero (BR-303); the validator of the use case should have refused it.", nameof(amount));
        }

        var allowed = CheckCanAllocate(amount);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var allocation = PaymentAllocation.Create(billId, amount, allocatedAtUtc, allocatedBy);
        _allocations.Add(allocation);
        UnappliedAmount -= amount;
        AddDomainEvent(new PaymentAllocated(Number, billId, amount, UnappliedAmount));

        return allocation;
    }

    /// <summary>
    /// Records which ledger posting belongs to <paramref name="allocation"/>. The use case calls it once for each
    /// allocation, between the ledger and the save.
    /// </summary>
    /// <exception cref="ArgumentException">The allocation is not one of this payment, or the posting id is empty.</exception>
    /// <exception cref="InvalidOperationException">The allocation has its posting already.</exception>
    public void AttachAllocationPosting(PaymentAllocation allocation, LedgerPostingId postingId)
    {
        ArgumentNullException.ThrowIfNull(allocation);

        if (!_allocations.Contains(allocation))
        {
            throw new ArgumentException("The allocation does not belong to this payment.", nameof(allocation));
        }

        allocation.AttachPosting(postingId);
    }

    /// <summary>
    /// A cashier asks for one allocation to be reversed (BR-305, US-32). The allocation waits for a second person;
    /// its amount stays applied to the bill until the approval. Refused when the payment itself waits for a reversal
    /// or is reversed, when it has no such allocation, or when the allocation already waits or is already reversed.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank or too long.</exception>
    public Result RequestAllocationReversal(PaymentAllocationId allocationId, string reason)
    {
        var why = Field.Required(reason, ReversalReasonMaxLength);

        if (RefuseUnlessPosted() is { } refused)
        {
            return refused;
        }

        var found = FindAllocation(allocationId, PaymentAllocationStatus.Active);
        if (found.IsFailure)
        {
            return found.Error;
        }

        found.Value.MarkPendingReversal();
        AddDomainEvent(new PaymentAllocationReversalRequested(Number, allocationId, found.Value.BillId, found.Value.Amount, why));

        return Result.Success();
    }

    /// <summary>The checker rejects the reversal of an allocation (BR-605): the allocation is in force again, as it was.</summary>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank or too long.</exception>
    public Result RejectAllocationReversal(PaymentAllocationId allocationId, string reason)
    {
        var why = Field.Required(reason, ReversalReasonMaxLength);

        var found = FindAllocation(allocationId, PaymentAllocationStatus.PendingReversal);
        if (found.IsFailure)
        {
            return found.Error;
        }

        found.Value.MarkActive();
        AddDomainEvent(new PaymentAllocationReversalRejected(Number, allocationId, why));

        return Result.Success();
    }

    /// <summary>
    /// Whether the reversal of allocation <paramref name="allocationId"/> can be decided: the payment has it, and it
    /// waits. For a use case that wants to know before it looks for the approval request.
    /// </summary>
    public Result CheckAllocationAwaitsReversal(PaymentAllocationId allocationId)
        => FindAllocation(allocationId, PaymentAllocationStatus.PendingReversal) is { IsFailure: true } missing ? missing.Error : Result.Success();

    /// <summary>
    /// The checker approves the reversal of an allocation (BR-305): the allocation is marked reversed, with who, when
    /// and why, and its amount is unapplied again. The bill and the mirror ledger posting are the use case's, in the
    /// same transaction. The row stays; nothing is deleted.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank or too long, or the checker's id is empty.</exception>
    public Result<PaymentAllocation> ReverseAllocation(PaymentAllocationId allocationId, string reason, DateTimeOffset reversedAtUtc, UserId reversedBy)
    {
        var why = Field.Required(reason, ReversalReasonMaxLength);
        RequireChecker(reversedBy);

        var found = FindAllocation(allocationId, PaymentAllocationStatus.PendingReversal);
        if (found.IsFailure)
        {
            return found.Error;
        }

        TakeBack(found.Value, why, reversedAtUtc, reversedBy);

        return found.Value;
    }

    /// <summary>
    /// A cashier asks for the payment to be reversed (BR-306, US-33), for example a dishonoured cheque. The payment
    /// waits for a second person and nothing can be allocated from it meanwhile. Refused when it already waits or is
    /// reversed, or when one of its allocations waits for a decision of its own: one decision at a time.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank or too long.</exception>
    public Result RequestReversal(string reason)
    {
        var why = Field.Required(reason, ReversalReasonMaxLength);

        if (RefuseUnlessPosted() is { } refused)
        {
            return refused;
        }

        if (_allocations.Any(allocation => allocation.Status == PaymentAllocationStatus.PendingReversal))
        {
            return PaymentsErrors.AllocationPendingReversal();
        }

        Status = PaymentStatus.PendingReversal;
        AddDomainEvent(new PaymentReversalRequested(Number, Amount, why));

        return Result.Success();
    }

    /// <summary>The checker rejects the reversal of the payment (BR-605): it is posted again, as it was.</summary>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank or too long.</exception>
    public Result RejectReversal(string reason)
    {
        var why = Field.Required(reason, ReversalReasonMaxLength);

        if (RefuseUnlessPendingReversal() is { } refused)
        {
            return refused;
        }

        Status = PaymentStatus.Posted;
        AddDomainEvent(new PaymentReversalRejected(Number, why));

        return Result.Success();
    }

    /// <summary>Whether the reversal of the payment can be decided: it waits for one.</summary>
    public Result CheckAwaitsReversal() => RefuseUnlessPendingReversal() is { } refused ? refused : Result.Success();

    /// <summary>
    /// The checker approves the reversal of the payment (BR-306): every allocation that is still applied is reversed
    /// first, so the whole amount is unapplied; then the payment itself is reversed and holds nothing. The answer is
    /// the allocations this took back, in the order of their bills, for the use case to restore each bill and enter
    /// each mirror posting before the posting of the payment itself.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank or too long, or the checker's id is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// Something other than allocations took money from the payment: a refund. A payment with a refund is not
    /// reversed (BR-306), and the use case must refuse it before it comes here.
    /// </exception>
    public Result<IReadOnlyList<PaymentAllocation>> Reverse(string reason, DateTimeOffset reversedAtUtc, UserId reversedBy)
    {
        var why = Field.Required(reason, ReversalReasonMaxLength);
        RequireChecker(reversedBy);

        if (RefuseUnlessPendingReversal() is { } refused)
        {
            return refused;
        }

        var applied = _allocations.Where(allocation => allocation.IsApplied).OrderBy(allocation => allocation.BillId.Value).ThenBy(allocation => allocation.Id.Value).ToArray();
        if (UnappliedAmount + applied.Select(allocation => allocation.Amount).Sum() != Amount)
        {
            throw new InvalidOperationException($"Payment {Number} cannot be reversed: part of it was refunded (BR-306).");
        }

        foreach (var allocation in applied)
        {
            TakeBack(allocation, why, reversedAtUtc, reversedBy);
        }

        UnappliedAmount = Money.Zero;
        Status = PaymentStatus.Reversed;
        ReversedAtUtc = reversedAtUtc;
        ReversedByUserId = reversedBy;
        ReversalReason = why;
        AddDomainEvent(new PaymentReversed(Number, PayerId, Amount, why));

        return applied;
    }

    /// <summary>Records the mirror ledger posting of a reversed allocation, once, between the ledger and the save.</summary>
    /// <exception cref="ArgumentException">The allocation is not one of this payment, or the posting id is empty.</exception>
    /// <exception cref="InvalidOperationException">The allocation is not reversed, or has its reversal posting already.</exception>
    public void AttachAllocationReversalPosting(PaymentAllocation allocation, LedgerPostingId postingId)
    {
        ArgumentNullException.ThrowIfNull(allocation);

        if (!_allocations.Contains(allocation))
        {
            throw new ArgumentException("The allocation does not belong to this payment.", nameof(allocation));
        }

        allocation.AttachReversalPosting(postingId);
    }

    /// <summary>Records the ledger posting of the payment's reversal, once, between the ledger and the save.</summary>
    /// <exception cref="ArgumentException">The posting id is empty.</exception>
    /// <exception cref="InvalidOperationException">The payment is not reversed, or has its reversal posting already.</exception>
    public void AttachReversalPosting(LedgerPostingId postingId)
    {
        if (postingId.Value == Guid.Empty)
        {
            throw new ArgumentException("A reversal belongs to a ledger posting; the empty id is not one.", nameof(postingId));
        }

        if (Status != PaymentStatus.Reversed || ReversalPostingId is not null)
        {
            throw new InvalidOperationException("Only a reversed payment takes its reversal posting, and only once (BR-006).");
        }

        ReversalPostingId = postingId;
    }

    // Marks one allocation reversed and gives its amount back to the payment.
    private void TakeBack(PaymentAllocation allocation, string reason, DateTimeOffset reversedAtUtc, UserId reversedBy)
    {
        allocation.MarkReversed(reason, reversedAtUtc, reversedBy);
        UnappliedAmount += allocation.Amount;
        AddDomainEvent(new PaymentAllocationReversed(Number, allocation.Id, allocation.BillId, allocation.Amount, UnappliedAmount, reason));
    }

    // The allocation with that id when it is in the state the caller needs; otherwise why not.
    private Result<PaymentAllocation> FindAllocation(PaymentAllocationId allocationId, PaymentAllocationStatus needed)
    {
        var allocation = _allocations.SingleOrDefault(candidate => candidate.Id == allocationId);
        if (allocation is null)
        {
            return PaymentsErrors.AllocationNotFound();
        }

        if (allocation.Status == needed)
        {
            return allocation;
        }

        return allocation.Status switch
        {
            PaymentAllocationStatus.Reversed => PaymentsErrors.AllocationReversed(),
            PaymentAllocationStatus.PendingReversal => PaymentsErrors.AllocationPendingReversal(),
            _ => PaymentsErrors.AllocationNotPendingReversal(),
        };
    }

    // Money moves in and out of a payment only while it is posted.
    private Error? RefuseUnlessPosted() => Status switch
    {
        PaymentStatus.Posted => null,
        PaymentStatus.PendingReversal => PaymentsErrors.PaymentPendingReversal(),
        _ => PaymentsErrors.PaymentReversed(),
    };

    // Only a payment that waits for its reversal has one to decide.
    private Error? RefuseUnlessPendingReversal() => Status switch
    {
        PaymentStatus.PendingReversal => null,
        PaymentStatus.Posted => PaymentsErrors.PaymentNotPendingReversal(),
        _ => PaymentsErrors.PaymentReversed(),
    };

    private static void RequireChecker(UserId reversedBy)
    {
        if (reversedBy.Value == Guid.Empty)
        {
            throw new ArgumentException("A reversal is approved by a user; the empty id is not one (BR-603).", nameof(reversedBy));
        }
    }
}
