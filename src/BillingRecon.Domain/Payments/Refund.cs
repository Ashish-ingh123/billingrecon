using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>
/// Money paid back to a payer (BR-307 to BR-309; docs/domain-model.md, section 4.7). A refund comes from exactly one
/// document of that payer: what a payment has not applied, or what a posted credit note has not applied. It belongs to
/// the payer of that document, carries a method and a reference as a payment does (BR-301), and has one amount.
/// <para>
/// Its life: a draft, which can be corrected and voided; then every refund waits for a second person (BR-604); the
/// checker's approval posts it under its gapless number, and from then on it is frozen and never reversed (BR-006,
/// BR-308).
/// </para>
/// <para>
/// The aggregate takes the id and the date of its source and what the source has left, never the source itself: the
/// use case reads them, a payment in this module and a credit note through the Billing module's contract. That the
/// source still has the amount at the moment of posting is the source's own rule, asked in the same transaction.
/// </para>
/// </summary>
public sealed class Refund : AggregateRoot<RefundId>, ITenantScoped, ICreationAudited, IModificationAudited, IHasRowVersion
{
    /// <summary>Longest allowed <see cref="Reason"/> and <see cref="VoidReason"/>.</summary>
    public const int ReasonMaxLength = 500;

    /// <summary>Longest allowed <see cref="Reference"/>, as for a payment.</summary>
    public const int ReferenceMaxLength = Payment.ReferenceMaxLength;

    /// <summary>The largest amount one refund may have: as much as a payment.</summary>
    public const decimal MaxAmount = Payment.MaxAmount;

    // Entity Framework creates a refund with this constructor and then sets the properties.
    private Refund()
    {
        Reason = string.Empty;
        Status = RefundStatus.Draft;
        RowVersion = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The payer the money goes back to: the payer of the source. It never changes.</summary>
    public PayerId PayerId { get; private set; }

    /// <summary>Which kind of document the money comes from. It never changes.</summary>
    public RefundSourceKind SourceKind { get; private set; }

    /// <summary>The payment the money comes from, when <see cref="SourceKind"/> says so; otherwise null.</summary>
    public PaymentId? SourcePaymentId { get; private set; }

    /// <summary>The credit note the money comes from, when <see cref="SourceKind"/> says so; otherwise null.</summary>
    public CreditNoteId? SourceCreditNoteId { get; private set; }

    /// <summary>The business date the money is paid back, a calendar date in India, not before the date of the source.</summary>
    public DateOnly RefundDate { get; private set; }

    /// <summary>How much, above zero.</summary>
    public Money Amount { get; private set; }

    /// <summary>How the money is paid back (BR-301).</summary>
    public PaymentMethod Method { get; private set; }

    /// <summary>The UPI id, card authorisation, UTR or cheque number; required unless the method is cash (BR-301).</summary>
    public string? Reference { get; private set; }

    /// <summary>Why the money is paid back, in the maker's words.</summary>
    public string Reason { get; private set; }

    /// <summary>Where the refund is in its life.</summary>
    public RefundStatus Status { get; private set; }

    /// <summary>When the draft was voided; null unless it was.</summary>
    public DateTimeOffset? VoidedAtUtc { get; private set; }

    /// <summary>Who voided the draft; null when it was not voided, or no user did it.</summary>
    public UserId? VoidedByUserId { get; private set; }

    /// <summary>Why the draft was voided; null unless it was.</summary>
    public string? VoidReason { get; private set; }

    /// <summary>The gapless number of the refund (BR-801); null until it is posted.</summary>
    public string? Number { get; private set; }

    /// <summary>The fiscal year of the refund date, which the number belongs to (BR-004); null until it is posted.</summary>
    public FiscalYear? FiscalYear { get; private set; }

    /// <summary>The position in the series of its fiscal year, from 1 (BR-801); null until it is posted.</summary>
    public int? SequenceNumber { get; private set; }

    /// <summary>When the refund was posted; null until it is.</summary>
    public DateTimeOffset? PostedAtUtc { get; private set; }

    /// <summary>Who posted it, which is the checker who approved it; null until it is posted.</summary>
    public UserId? PostedByUserId { get; private set; }

    /// <summary>
    /// The ledger posting of the refund: Dr UnappliedCash(payment) or CreditAvailable(credit note), Cr Cash (BR-307);
    /// null until it is posted.
    /// </summary>
    public LedgerPostingId? PostingId { get; private set; }

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

    /// <summary>The document the money comes from.</summary>
    public RefundSource Source => SourceKind == RefundSourceKind.Payment
        ? RefundSource.FromPayment(SourcePaymentId ?? default)
        : RefundSource.FromCreditNote(SourceCreditNoteId ?? default);

    /// <summary>
    /// Creates a draft that pays back <paramref name="amount"/> from <paramref name="source"/>, a document of
    /// <paramref name="payerId"/> dated <paramref name="sourceDate"/>. The date must lie in <paramref name="window"/>
    /// (BR-205) and not before the source's date (BR-307); the first rule that fails is returned and nothing is
    /// created. Whether the source has the amount is asked when the maker submits and when the checker approves.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The source or the payer is empty, the amount is not above zero or too large, the method is not defined, a method
    /// other than cash has no reference, or a text is blank or too long. The validator of the use case refuses all of
    /// these first.
    /// </exception>
    public static Result<Refund> Create(
        RefundSource source,
        PayerId payerId,
        DateOnly sourceDate,
        DateOnly refundDate,
        Money amount,
        PaymentMethod method,
        string? reference,
        string reason,
        BusinessDateWindow window)
    {
        if (source.Id == Guid.Empty || payerId.Value == Guid.Empty)
        {
            throw new ArgumentException("A refund comes from a payment or a credit note of a payer; the empty id is neither.", nameof(source));
        }

        var why = Field.Required(reason, ReasonMaxLength);
        var trimmedReference = CheckPayout(amount, method, reference);

        if (CheckDate(refundDate, sourceDate, window) is { } refused)
        {
            return refused;
        }

        var refund = new Refund
        {
            PayerId = payerId,
            SourceKind = source.Kind,
            SourcePaymentId = source.PaymentId,
            SourceCreditNoteId = source.CreditNoteId,
            RefundDate = refundDate,
            Amount = amount,
            Method = method,
            Reference = trimmedReference,
            Reason = why,
        };

        refund.AddDomainEvent(new RefundDrafted(payerId, source.Kind, source.Id, amount, method, refundDate));

        return refund;
    }

    /// <summary>
    /// Replaces the date, the amount, the method, the reference and the reason of a draft, under the rules of
    /// <see cref="Create"/>; the source never changes. A date that stays as it is is not checked against the window
    /// again; submitting checks it once more. Raises <see cref="RefundUpdated"/> when a value changed.
    /// </summary>
    /// <exception cref="ArgumentException">A value breaks a limit the validator checks.</exception>
    public Result Update(DateOnly refundDate, Money amount, PaymentMethod method, string? reference, string reason, DateOnly sourceDate, BusinessDateWindow window)
    {
        var why = Field.Required(reason, ReasonMaxLength);
        var trimmedReference = CheckPayout(amount, method, reference);

        if (RefuseUnlessDraft() is { } refused)
        {
            return refused;
        }

        if (refundDate != RefundDate && CheckDate(refundDate, sourceDate, window) is { } refusedDate)
        {
            return refusedDate;
        }

        if ((refundDate, amount, method, trimmedReference, why) != (RefundDate, Amount, Method, Reference, Reason))
        {
            RefundDate = refundDate;
            Amount = amount;
            Method = method;
            Reference = trimmedReference;
            Reason = why;
            AddDomainEvent(new RefundUpdated(Id, Amount, Method, RefundDate));
        }

        return Result.Success();
    }

    /// <summary>Voids a draft: it keeps its content and can no longer change. A refund that is not a draft is refused.</summary>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank or too long.</exception>
    public Result Void(string reason, DateTimeOffset voidedAtUtc, UserId? voidedBy)
    {
        var why = Field.Required(reason, ReasonMaxLength);

        if (RefuseUnlessDraft() is { } refused)
        {
            return refused;
        }

        Status = RefundStatus.Voided;
        VoidedAtUtc = voidedAtUtc;
        VoidedByUserId = voidedBy;
        VoidReason = why;
        AddDomainEvent(new RefundVoided(Id, Amount, why));

        return Result.Success();
    }

    /// <summary>
    /// Hands a draft over to a second person (BR-604): it is a draft, and its date still lies in
    /// <paramref name="window"/> and not before <paramref name="sourceDate"/>. The refund is locked from here on, keeps
    /// no number, and has no ledger effect until the checker approves. Whether the source has the amount is asked by
    /// the use case, which knows the source (<see cref="CheckFits"/>).
    /// </summary>
    public Result SubmitForApproval(DateOnly sourceDate, BusinessDateWindow window)
    {
        if (RefuseUnlessDraft() is { } refused)
        {
            return refused;
        }

        if (CheckDate(RefundDate, sourceDate, window) is { } refusedDate)
        {
            return refusedDate;
        }

        Status = RefundStatus.PendingApproval;
        AddDomainEvent(new RefundSubmittedForApproval(Id, Amount));

        return Result.Success();
    }

    /// <summary>
    /// Whether the amount is not more than <paramref name="available"/>, what the source
    /// <paramref name="sourceNumber"/> has left (BR-307). Asked when the maker submits and again when the checker
    /// approves.
    /// </summary>
    public Result CheckFits(string sourceNumber, Money available)
        => Amount > available ? PaymentsErrors.RefundExceedsAvailable(sourceNumber, available) : Result.Success();

    /// <summary>
    /// Whether the checker can approve the refund today (BR-606): it waits for approval, and its date is still
    /// allowed, which is checked again because it may have waited (BR-205).
    /// </summary>
    public Result CheckCanApprove(DateOnly sourceDate, BusinessDateWindow window)
    {
        if (RefuseUnlessPending() is { } refused)
        {
            return refused;
        }

        return CheckDate(RefundDate, sourceDate, window) is { } refusedDate ? refusedDate : Result.Success();
    }

    /// <summary>
    /// Posts a refund that waited for approval, under <paramref name="number"/> (BR-307, BR-606): the checker's
    /// approval is the posting. The refund is frozen. <paramref name="postedBy"/> is the checker.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="number"/> is not a refund number of the fiscal year of the refund date: a bug in the caller.
    /// </exception>
    public Result PostApproved(DocumentNumber number, DateTimeOffset postedAtUtc, UserId? postedBy, DateOnly sourceDate, BusinessDateWindow window)
    {
        ArgumentNullException.ThrowIfNull(number);

        var allowed = CheckCanApprove(sourceDate, window);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        if (number.Type != DocumentType.Refund || number.FiscalYear != Common.FiscalYear.FromDate(RefundDate))
        {
            throw new ArgumentException("A refund is posted under a refund number of the fiscal year of its date (BR-307).", nameof(number));
        }

        Number = number.Value;
        FiscalYear = number.FiscalYear;
        SequenceNumber = number.Sequence;
        PostedAtUtc = postedAtUtc;
        PostedByUserId = postedBy;
        Status = RefundStatus.Posted;
        AddDomainEvent(new RefundPosted(Id, number.Value, PayerId, SourceKind, Source.Id, Amount, Method, Reason));

        return Result.Success();
    }

    /// <summary>Records which ledger posting belongs to the refund, once, between the ledger and the save.</summary>
    /// <exception cref="ArgumentException">The posting id is empty.</exception>
    /// <exception cref="InvalidOperationException">The refund is not posted, or has its posting already.</exception>
    public void AttachPosting(LedgerPostingId postingId)
    {
        if (postingId.Value == Guid.Empty)
        {
            throw new ArgumentException("A refund belongs to a ledger posting; the empty id is not one.", nameof(postingId));
        }

        if (Status != RefundStatus.Posted || PostingId is not null)
        {
            throw new InvalidOperationException("Only a posted refund takes its ledger posting, and only once (BR-006).");
        }

        PostingId = postingId;
    }

    /// <summary>The checker rejected the refund (BR-605): it is a draft again and can be corrected, voided or submitted once more.</summary>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank or too long.</exception>
    public Result ReturnToDraft(string reason)
    {
        var why = Field.Required(reason, ReasonMaxLength);

        if (RefuseUnlessPending() is { } refused)
        {
            return refused;
        }

        Status = RefundStatus.Draft;
        AddDomainEvent(new RefundReturnedToDraft(Id, why));

        return Result.Success();
    }

    /// <summary>Whether the refund can still be changed or submitted: it is a draft. For a use case that wants to know before it does other work.</summary>
    public Result CheckIsDraft() => RefuseUnlessDraft() is { } refused ? refused : Result.Success();

    // Only a draft changes; every other state refuses with its own answer.
    private Error? RefuseUnlessDraft() => Status switch
    {
        RefundStatus.Draft => null,
        RefundStatus.Voided => PaymentsErrors.RefundNotDraft(),
        RefundStatus.PendingApproval => PaymentsErrors.RefundPendingApproval(),
        _ => PaymentsErrors.RefundAlreadyPosted(),
    };

    // Only a refund that waits is approved or rejected.
    private Error? RefuseUnlessPending() => Status switch
    {
        RefundStatus.PendingApproval => null,
        RefundStatus.Draft => PaymentsErrors.RefundNotPendingApproval(),
        RefundStatus.Voided => PaymentsErrors.RefundNotDraft(),
        _ => PaymentsErrors.RefundAlreadyPosted(),
    };

    private static Error? CheckDate(DateOnly refundDate, DateOnly sourceDate, BusinessDateWindow window)
    {
        if (refundDate > window.Latest)
        {
            return PaymentsErrors.RefundDateInFuture();
        }

        if (refundDate < window.Earliest)
        {
            return PaymentsErrors.RefundDateTooOld(window.Earliest);
        }

        return refundDate < sourceDate ? PaymentsErrors.RefundDateBeforeSourceDate(sourceDate) : null;
    }

    // The amount, the method and the reference under the rules of a payment (BR-301); the validator of the use case
    // refuses every value that breaks them, so a failure here is a bug in the caller. Answers the trimmed reference.
    private static string? CheckPayout(Money amount, PaymentMethod method, string? reference)
    {
        if (!amount.IsPositive || amount.Amount > MaxAmount)
        {
            throw new ArgumentException($"A refund has an amount above zero and at most {MaxAmount} (BR-307).", nameof(amount));
        }

        if (!Enum.IsDefined(method))
        {
            throw new ArgumentOutOfRangeException(nameof(method), method, "The refund method is not one the enum defines.");
        }

        var trimmed = Field.Optional(reference, ReferenceMaxLength);
        if (method != PaymentMethod.Cash && trimmed is null)
        {
            throw new ArgumentException("A refund that is not cash has a reference (BR-301).", nameof(reference));
        }

        return trimmed;
    }
}
