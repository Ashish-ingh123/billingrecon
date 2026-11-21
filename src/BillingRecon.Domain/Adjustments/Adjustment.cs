using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Adjustments;

/// <summary>
/// An adjustment: a posted bill lowered by a discount, a write-off or a rounding difference (BR-501 to BR-503;
/// docs/domain-model.md, section 4.8). It is against exactly one posted bill and belongs to that bill's payer; it has
/// one amount and no lines. An adjustment only ever lowers a bill (BR-503).
/// <para>
/// Its life: a draft, which can be corrected and voided; then every adjustment waits for a second person (BR-502,
/// BR-604); the checker's approval posts it under its gapless number, and from then on it is frozen (BR-006).
/// </para>
/// <para>
/// The aggregate takes the id and the date of its bill, and what the bill is still owed, never the bill itself
/// (ADR 0003): the use case reads them through the Billing module's contract. That the amount still fits the balance
/// at the moment of posting is the bill's own rule, asked in the same transaction.
/// </para>
/// </summary>
public sealed class Adjustment : AggregateRoot<AdjustmentId>, ITenantScoped, ICreationAudited, IModificationAudited, IHasRowVersion
{
    /// <summary>Longest allowed <see cref="Reason"/> and <see cref="VoidReason"/>.</summary>
    public const int ReasonMaxLength = 500;

    /// <summary>The largest amount one adjustment may have: as much as a payment.</summary>
    public const decimal MaxAmount = 9_999_999_999.99m;

    /// <summary>A rounding adjustment is less than this (BR-501).</summary>
    public const decimal RoundingLimit = 1.00m;

    // Also the constructor Entity Framework uses to load an adjustment: it passes the two stored ids and then sets
    // the remaining properties.
    private Adjustment(BillId billId, PayerId payerId)
    {
        BillId = billId;
        PayerId = payerId;
        Reason = string.Empty;
        Status = AdjustmentStatus.Draft;
        RowVersion = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The posted bill the adjustment lowers (BR-501). It never changes.</summary>
    public BillId BillId { get; private set; }

    /// <summary>The payer of that bill. It never changes.</summary>
    public PayerId PayerId { get; private set; }

    /// <summary>The business date of the adjustment, a calendar date in India, not before the bill date.</summary>
    public DateOnly AdjustmentDate { get; private set; }

    /// <summary>Why the bill is lowered: a discount, a write-off or a rounding difference.</summary>
    public AdjustmentKind Kind { get; private set; }

    /// <summary>By how much, above zero; less than one rupee for a rounding (BR-501).</summary>
    public Money Amount { get; private set; }

    /// <summary>The reason, in the maker's words (BR-502).</summary>
    public string Reason { get; private set; }

    /// <summary>Where the adjustment is in its life.</summary>
    public AdjustmentStatus Status { get; private set; }

    /// <summary>When the draft was voided; null unless it was.</summary>
    public DateTimeOffset? VoidedAtUtc { get; private set; }

    /// <summary>Who voided the draft; null when it was not voided, or no user did it.</summary>
    public UserId? VoidedByUserId { get; private set; }

    /// <summary>Why the draft was voided; null unless it was.</summary>
    public string? VoidReason { get; private set; }

    /// <summary>The gapless number of the adjustment (BR-801); null until it is posted.</summary>
    public string? Number { get; private set; }

    /// <summary>The fiscal year of the adjustment date, which the number belongs to (BR-004); null until it is posted.</summary>
    public FiscalYear? FiscalYear { get; private set; }

    /// <summary>The position in the series of its fiscal year, from 1 (BR-801); null until it is posted.</summary>
    public int? SequenceNumber { get; private set; }

    /// <summary>When the adjustment was posted; null until it is.</summary>
    public DateTimeOffset? PostedAtUtc { get; private set; }

    /// <summary>Who posted it, which is the checker who approved it; null until it is posted.</summary>
    public UserId? PostedByUserId { get; private set; }

    /// <summary>The ledger posting of the adjustment: Dr Adjustments, Cr Receivable(bill) (BR-502); null until it is posted.</summary>
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

    /// <summary>
    /// Creates a draft against the posted bill <paramref name="billId"/> of <paramref name="payerId"/>. The date must
    /// lie in <paramref name="window"/> (BR-205) and not before <paramref name="billDate"/>, and a rounding must be
    /// less than one rupee (BR-501); the first rule that fails is returned and nothing is created.
    /// </summary>
    /// <exception cref="ArgumentException">An id is empty, the amount is not above zero or is too large, the kind is not defined, or the reason is blank or too long.</exception>
    public static Result<Adjustment> Create(
        BillId billId,
        PayerId payerId,
        DateOnly billDate,
        DateOnly adjustmentDate,
        AdjustmentKind kind,
        Money amount,
        string reason,
        BusinessDateWindow window)
    {
        if (billId.Value == Guid.Empty || payerId.Value == Guid.Empty)
        {
            throw new ArgumentException("An adjustment is against a bill, with its payer; the empty id is neither.", nameof(billId));
        }

        var why = Field.Required(reason, ReasonMaxLength);

        if (Check(adjustmentDate, kind, amount, billDate, window) is { } refused)
        {
            return refused;
        }

        var adjustment = new Adjustment(billId, payerId) { AdjustmentDate = adjustmentDate, Kind = kind, Amount = amount, Reason = why };

        // The id is still empty here: it is assigned when the adjustment is added to the unit of work.
        adjustment.AddDomainEvent(new AdjustmentDrafted(billId, payerId, kind, amount, adjustmentDate));

        return adjustment;
    }

    /// <summary>
    /// Replaces the date, the kind, the amount and the reason of a draft, under the rules of <see cref="Create"/>. A
    /// date that stays as it is is not checked against the window again; submitting checks it once more. Raises
    /// <see cref="AdjustmentUpdated"/> when a value changed.
    /// </summary>
    /// <exception cref="ArgumentException">A value breaks a limit the validator checks.</exception>
    public Result Update(DateOnly adjustmentDate, AdjustmentKind kind, Money amount, string reason, DateOnly billDate, BusinessDateWindow window)
    {
        var why = Field.Required(reason, ReasonMaxLength);

        if (RefuseUnlessDraft() is { } refused)
        {
            return refused;
        }

        var refusedValues = adjustmentDate == AdjustmentDate ? CheckAmount(kind, amount) : Check(adjustmentDate, kind, amount, billDate, window);
        if (refusedValues is not null)
        {
            return refusedValues;
        }

        if ((adjustmentDate, kind, amount, why) != (AdjustmentDate, Kind, Amount, Reason))
        {
            AdjustmentDate = adjustmentDate;
            Kind = kind;
            Amount = amount;
            Reason = why;
            AddDomainEvent(new AdjustmentUpdated(Id, Kind, Amount, AdjustmentDate));
        }

        return Result.Success();
    }

    /// <summary>Voids a draft: it keeps its content and can no longer change. An adjustment that is not a draft is refused.</summary>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank or too long.</exception>
    public Result Void(string reason, DateTimeOffset voidedAtUtc, UserId? voidedBy)
    {
        var why = Field.Required(reason, ReasonMaxLength);

        if (RefuseUnlessDraft() is { } refused)
        {
            return refused;
        }

        Status = AdjustmentStatus.Voided;
        VoidedAtUtc = voidedAtUtc;
        VoidedByUserId = voidedBy;
        VoidReason = why;
        AddDomainEvent(new AdjustmentVoided(Id, BillId, Amount, why));

        return Result.Success();
    }

    /// <summary>
    /// Hands a draft over to a second person (BR-502, BR-604): it is a draft, and its date still lies in
    /// <paramref name="window"/> and not before <paramref name="billDate"/>. The adjustment is locked from here on,
    /// keeps no number, and has no ledger effect until the checker approves. Whether the amount fits what the bill
    /// is owed is asked by the use case, which knows the bill (<see cref="CheckFits"/>).
    /// </summary>
    public Result SubmitForApproval(DateOnly billDate, BusinessDateWindow window)
    {
        if (RefuseUnlessDraft() is { } refused)
        {
            return refused;
        }

        if (CheckDate(AdjustmentDate, billDate, window) is { } refusedDate)
        {
            return refusedDate;
        }

        Status = AdjustmentStatus.PendingApproval;
        AddDomainEvent(new AdjustmentSubmittedForApproval(Id, BillId, Amount));

        return Result.Success();
    }

    /// <summary>
    /// Whether the amount is not more than <paramref name="balanceDue"/>, what bill <paramref name="billNumber"/> is
    /// still owed (BR-501). Asked when the maker submits and again when the checker approves.
    /// </summary>
    public Result CheckFits(string billNumber, Money balanceDue)
        => Amount > balanceDue ? AdjustmentsErrors.AdjustmentExceedsBalanceDue(billNumber, balanceDue) : Result.Success();

    /// <summary>
    /// Whether the checker can approve the adjustment today (BR-606): it waits for approval, and its date is still
    /// allowed, which is checked again because it may have waited (BR-205).
    /// </summary>
    public Result CheckCanApprove(DateOnly billDate, BusinessDateWindow window)
    {
        if (RefuseUnlessPending() is { } refused)
        {
            return refused;
        }

        return CheckDate(AdjustmentDate, billDate, window) is { } refusedDate ? refusedDate : Result.Success();
    }

    /// <summary>
    /// Posts an adjustment that waited for approval, under <paramref name="number"/> (BR-502, BR-606): the checker's
    /// approval is the posting. The adjustment is frozen. <paramref name="postedBy"/> is the checker.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="number"/> is not an adjustment number of the fiscal year of the adjustment date: a bug in the caller.
    /// </exception>
    public Result PostApproved(DocumentNumber number, DateTimeOffset postedAtUtc, UserId? postedBy, DateOnly billDate, BusinessDateWindow window)
    {
        ArgumentNullException.ThrowIfNull(number);

        var allowed = CheckCanApprove(billDate, window);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        if (number.Type != DocumentType.Adjustment || number.FiscalYear != Common.FiscalYear.FromDate(AdjustmentDate))
        {
            throw new ArgumentException("An adjustment is posted under an adjustment number of the fiscal year of its date (BR-502).", nameof(number));
        }

        Number = number.Value;
        FiscalYear = number.FiscalYear;
        SequenceNumber = number.Sequence;
        PostedAtUtc = postedAtUtc;
        PostedByUserId = postedBy;
        Status = AdjustmentStatus.Posted;
        AddDomainEvent(new AdjustmentPosted(Id, number.Value, BillId, PayerId, Kind, Amount, Reason));

        return Result.Success();
    }

    /// <summary>Records which ledger posting belongs to the adjustment, once, between the ledger and the save.</summary>
    /// <exception cref="ArgumentException">The posting id is empty.</exception>
    /// <exception cref="InvalidOperationException">The adjustment is not posted, or has its posting already.</exception>
    public void AttachPosting(LedgerPostingId postingId)
    {
        if (postingId.Value == Guid.Empty)
        {
            throw new ArgumentException("An adjustment belongs to a ledger posting; the empty id is not one.", nameof(postingId));
        }

        if (Status != AdjustmentStatus.Posted || PostingId is not null)
        {
            throw new InvalidOperationException("Only a posted adjustment takes its ledger posting, and only once (BR-006).");
        }

        PostingId = postingId;
    }

    /// <summary>The checker rejected the adjustment (BR-605): it is a draft again and can be corrected, voided or submitted once more.</summary>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank or too long.</exception>
    public Result ReturnToDraft(string reason)
    {
        var why = Field.Required(reason, ReasonMaxLength);

        if (RefuseUnlessPending() is { } refused)
        {
            return refused;
        }

        Status = AdjustmentStatus.Draft;
        AddDomainEvent(new AdjustmentReturnedToDraft(Id, why));

        return Result.Success();
    }

    /// <summary>Whether the adjustment can still be submitted: it is a draft. For a use case that wants to know before it does other work.</summary>
    public Result CheckIsDraft() => RefuseUnlessDraft() is { } refused ? refused : Result.Success();

    // Only a draft changes; every other state refuses with its own answer.
    private Error? RefuseUnlessDraft() => Status switch
    {
        AdjustmentStatus.Draft => null,
        AdjustmentStatus.Voided => AdjustmentsErrors.AdjustmentNotDraft(),
        AdjustmentStatus.PendingApproval => AdjustmentsErrors.AdjustmentPendingApproval(),
        _ => AdjustmentsErrors.AdjustmentAlreadyPosted(),
    };

    // Only an adjustment that waits is approved or rejected.
    private Error? RefuseUnlessPending() => Status switch
    {
        AdjustmentStatus.PendingApproval => null,
        AdjustmentStatus.Draft => AdjustmentsErrors.AdjustmentNotPendingApproval(),
        AdjustmentStatus.Voided => AdjustmentsErrors.AdjustmentNotDraft(),
        _ => AdjustmentsErrors.AdjustmentAlreadyPosted(),
    };

    private static Error? Check(DateOnly adjustmentDate, AdjustmentKind kind, Money amount, DateOnly billDate, BusinessDateWindow window)
        => CheckDate(adjustmentDate, billDate, window) ?? CheckAmount(kind, amount);

    private static Error? CheckDate(DateOnly adjustmentDate, DateOnly billDate, BusinessDateWindow window)
    {
        if (adjustmentDate > window.Latest)
        {
            return AdjustmentsErrors.AdjustmentDateInFuture();
        }

        if (adjustmentDate < window.Earliest)
        {
            return AdjustmentsErrors.AdjustmentDateTooOld(window.Earliest);
        }

        return adjustmentDate < billDate ? AdjustmentsErrors.AdjustmentDateBeforeBillDate(billDate) : null;
    }

    // The validator of the use case refuses an amount that is not above zero or too large, and a kind nobody defined.
    private static Error? CheckAmount(AdjustmentKind kind, Money amount)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException("The kind of an adjustment is one the enum defines.", nameof(kind));
        }

        if (!amount.IsPositive || amount.Amount > MaxAmount)
        {
            throw new ArgumentException("An adjustment has an amount above zero and at most 9999999999.99 (BR-501).", nameof(amount));
        }

        return kind == AdjustmentKind.Rounding && amount.Amount >= RoundingLimit ? AdjustmentsErrors.AdjustmentRoundingTooLarge() : null;
    }
}
