using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// A credit note: a correction that lowers what was billed (BR-209 to BR-214; docs/domain-model.md, section 4.5). It
/// is written against exactly one posted bill and belongs to that bill's payer (BR-210); its lines have the
/// arithmetic of bill lines (BR-202) and its totals are the sums of its lines (BR-203).
/// <para>
/// Its life: a draft, which can be edited and voided; then every credit note waits for a second person (BR-212,
/// BR-604); the checker's approval posts it under its gapless number, and from then on it is frozen (BR-006). A posted
/// credit note is credit of the payer: <see cref="AvailableAmount"/> starts at <see cref="Total"/> and falls as the
/// credit is applied to bills, first to its own bill, by the use case that posts it (BR-212, BR-213).
/// </para>
/// <para>
/// The aggregate takes the id and the date of its bill, never the bill (ADR 0003). Whether the bill is posted, and
/// whether the credits of a bill stay within it (BR-210, BR-211), are questions the use case answers with what it
/// read; <see cref="CheckWithinBill"/> holds the rule.
/// </para>
/// </summary>
public sealed class CreditNote : AggregateRoot<CreditNoteId>, ITenantScoped, ICreationAudited, IModificationAudited, IHasRowVersion
{
    /// <summary>The most lines a credit note may have: as many as a bill.</summary>
    public const int MaxLines = Bill.MaxLines;

    /// <summary>Longest allowed <see cref="Reason"/> and <see cref="VoidReason"/>.</summary>
    public const int ReasonMaxLength = 500;

    private readonly List<CreditNoteLine> _lines = [];
    private readonly List<CreditNoteAllocation> _allocations = [];

    // Also the constructor Entity Framework uses to load a credit note: it passes the three stored ids and then sets
    // the remaining properties and fills the children.
    private CreditNote(BillId billId, PayerId payerId, PatientId patientId)
    {
        BillId = billId;
        PayerId = payerId;
        PatientId = patientId;
        Reason = string.Empty;
        Status = CreditNoteStatus.Draft;
        RowVersion = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The posted bill the credit note is against (BR-210). It never changes.</summary>
    public BillId BillId { get; private set; }

    /// <summary>The payer of that bill, whose credit this is. It never changes.</summary>
    public PayerId PayerId { get; private set; }

    /// <summary>The patient of that bill. It never changes.</summary>
    public PatientId PatientId { get; private set; }

    /// <summary>The business date of the credit note, a calendar date in India, not before the bill date (BR-214).</summary>
    public DateOnly CreditNoteDate { get; private set; }

    /// <summary>Why the credit is given.</summary>
    public string Reason { get; private set; }

    /// <summary>Where the credit note is in its life.</summary>
    public CreditNoteStatus Status { get; private set; }

    /// <summary>The sum of the line amounts (BR-203).</summary>
    public Money SubTotal { get; private set; }

    /// <summary>The sum of the line discounts (BR-203).</summary>
    public Money DiscountTotal { get; private set; }

    /// <summary>The sum of the line taxes (BR-203).</summary>
    public Money TaxTotal { get; private set; }

    /// <summary>What the credit note comes to: <c>SubTotal - DiscountTotal + TaxTotal</c>, never negative (BR-203).</summary>
    public Money Total { get; private set; }

    /// <summary>
    /// The credit that is not applied yet (BR-213, BR-405): between zero and <see cref="Total"/>. Until the credit note
    /// is posted there is no credit, and this simply equals the total.
    /// </summary>
    public Money AvailableAmount { get; private set; }

    /// <summary>When the draft was voided; null unless it was.</summary>
    public DateTimeOffset? VoidedAtUtc { get; private set; }

    /// <summary>Who voided the draft; null when it was not voided, or no user did it.</summary>
    public UserId? VoidedByUserId { get; private set; }

    /// <summary>Why the draft was voided; null unless it was.</summary>
    public string? VoidReason { get; private set; }

    /// <summary>The gapless number of the credit note (BR-801); null until it is posted.</summary>
    public string? Number { get; private set; }

    /// <summary>The fiscal year of the credit note date, which the number belongs to (BR-004); null until it is posted.</summary>
    public FiscalYear? FiscalYear { get; private set; }

    /// <summary>The position in the series of its fiscal year, from 1 (BR-801); null until it is posted.</summary>
    public int? SequenceNumber { get; private set; }

    /// <summary>When the credit note was posted; null until it is.</summary>
    public DateTimeOffset? PostedAtUtc { get; private set; }

    /// <summary>Who posted it, which is the checker who approved it; null until it is posted.</summary>
    public UserId? PostedByUserId { get; private set; }

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

    /// <summary>The lines, in the order of their numbers. A draft may have none; a credit note needs one to be submitted.</summary>
    public IReadOnlyList<CreditNoteLine> Lines => _lines;

    /// <summary>The applications of the credit to bills, in no promised order. None until the credit note is posted.</summary>
    public IReadOnlyList<CreditNoteAllocation> Allocations => _allocations;

    /// <summary>
    /// Creates a draft against the posted bill <paramref name="billId"/> of <paramref name="payerId"/>, dated
    /// <paramref name="creditNoteDate"/>, which must lie in <paramref name="window"/> (BR-205) and not before
    /// <paramref name="billDate"/> (BR-214). The first rule that fails is returned and nothing is created.
    /// </summary>
    /// <exception cref="ArgumentException">An id is empty, or a value breaks a limit the validator checks.</exception>
    public static Result<CreditNote> Create(
        BillId billId,
        PayerId payerId,
        PatientId patientId,
        DateOnly billDate,
        DateOnly creditNoteDate,
        string reason,
        IReadOnlyList<CreditNoteLineDetails> lines,
        BusinessDateWindow window)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (billId.Value == Guid.Empty || payerId.Value == Guid.Empty || patientId.Value == Guid.Empty)
        {
            throw new ArgumentException("A credit note is against a bill, with its payer and its patient; the empty id is none of them.", nameof(billId));
        }

        var why = Field.Required(reason, ReasonMaxLength);

        if (CheckDate(creditNoteDate, billDate, window) is { } refusedDate)
        {
            return refusedDate;
        }

        var checkedLines = CheckLines(lines);
        if (checkedLines.IsFailure)
        {
            return checkedLines.Error;
        }

        var creditNote = new CreditNote(billId, payerId, patientId) { CreditNoteDate = creditNoteDate, Reason = why };
        creditNote.SetLines(lines, checkedLines.Value);

        // The id is still empty here: it is assigned when the credit note is added to the unit of work.
        creditNote.AddDomainEvent(new CreditNoteDrafted(billId, payerId, creditNoteDate, creditNote.Total));

        return creditNote;
    }

    /// <summary>
    /// Replaces the date and the reason of a draft. A date that changes must lie in <paramref name="window"/> and not
    /// before <paramref name="billDate"/>; one that stays as it is is not checked again, and submitting checks it once
    /// more. Raises <see cref="CreditNoteHeaderUpdated"/> when a value changed.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank or too long.</exception>
    public Result UpdateHeader(DateOnly creditNoteDate, string reason, DateOnly billDate, BusinessDateWindow window)
    {
        var why = Field.Required(reason, ReasonMaxLength);

        if (RefuseUnlessDraft() is { } refused)
        {
            return refused;
        }

        if (creditNoteDate != CreditNoteDate && CheckDate(creditNoteDate, billDate, window) is { } refusedDate)
        {
            return refusedDate;
        }

        if ((creditNoteDate, why) != (CreditNoteDate, Reason))
        {
            CreditNoteDate = creditNoteDate;
            Reason = why;
            AddDomainEvent(new CreditNoteHeaderUpdated(Id, CreditNoteDate));
        }

        return Result.Success();
    }

    /// <summary>
    /// Replaces every line of a draft with <paramref name="lines"/>, in the order given, and recomputes the totals.
    /// The first line that is refused is returned and the credit note stays as it was. Raises
    /// <see cref="CreditNoteLinesReplaced"/> when a line changed.
    /// </summary>
    /// <exception cref="ArgumentException">There are more than <see cref="MaxLines"/> lines, or a value breaks a limit the validator checks.</exception>
    public Result ReplaceLines(IReadOnlyList<CreditNoteLineDetails> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (RefuseUnlessDraft() is { } refused)
        {
            return refused;
        }

        var checkedLines = CheckLines(lines);
        if (checkedLines.IsFailure)
        {
            return checkedLines.Error;
        }

        var before = LineContents();
        SetLines(lines, checkedLines.Value);

        if (!before.SequenceEqual(LineContents()))
        {
            AddDomainEvent(new CreditNoteLinesReplaced(Id, _lines.Count, Total));
        }

        return Result.Success();
    }

    /// <summary>Voids a draft: it keeps its content and can no longer change. A credit note that is not a draft is refused.</summary>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank or too long.</exception>
    public Result Void(string reason, DateTimeOffset voidedAtUtc, UserId? voidedBy)
    {
        var why = Field.Required(reason, ReasonMaxLength);

        if (RefuseUnlessDraft() is { } refused)
        {
            return refused;
        }

        Status = CreditNoteStatus.Voided;
        VoidedAtUtc = voidedAtUtc;
        VoidedByUserId = voidedBy;
        VoidReason = why;
        AddDomainEvent(new CreditNoteVoided(Id, BillId, Total, why));

        return Result.Success();
    }

    /// <summary>
    /// Whether the draft can be handed to a second person today: it is a draft, has a line, comes to more than
    /// nothing, and its date still lies in <paramref name="window"/> and not before <paramref name="billDate"/>.
    /// </summary>
    public Result CheckCanSubmit(DateOnly billDate, BusinessDateWindow window)
    {
        if (RefuseUnlessDraft() is { } refused)
        {
            return refused;
        }

        if (_lines.Count == 0)
        {
            return BillingErrors.CreditNoteHasNoLines();
        }

        if (!Total.IsPositive)
        {
            return BillingErrors.CreditNoteTotalZero();
        }

        return CheckDate(CreditNoteDate, billDate, window) is { } refusedDate ? refusedDate : Result.Success();
    }

    /// <summary>
    /// Hands a draft over to a second person (BR-212, BR-604), under the rules of <see cref="CheckCanSubmit"/>. The
    /// credit note is locked from here on, keeps no number, and has no ledger effect until the checker approves.
    /// </summary>
    public Result SubmitForApproval(DateOnly billDate, BusinessDateWindow window)
    {
        var allowed = CheckCanSubmit(billDate, window);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        Status = CreditNoteStatus.PendingApproval;
        AddDomainEvent(new CreditNoteSubmittedForApproval(Id, BillId, Total));

        return Result.Success();
    }

    /// <summary>
    /// Whether the checker can approve the credit note today (BR-606): it waits for approval, and its date is still
    /// allowed, which is checked again because it may have waited (BR-205, BR-214).
    /// </summary>
    public Result CheckCanApprove(DateOnly billDate, BusinessDateWindow window)
    {
        if (RefuseUnlessPending() is { } refused)
        {
            return refused;
        }

        return CheckDate(CreditNoteDate, billDate, window) is { } refusedDate ? refusedDate : Result.Success();
    }

    /// <summary>
    /// Whether the credits of the bill stay within the bill with this credit note (BR-210, BR-211): its total with
    /// <paramref name="alreadyCredited"/>, the totals of the bill's other posted credit notes, is not more than
    /// <paramref name="billTotal"/>; and for every line of the bill that its lines name, their line totals with
    /// what the other posted credit notes credit for that line are not more than the total of that line.
    /// </summary>
    /// <param name="billTotal">The total of the bill.</param>
    /// <param name="alreadyCredited">The sum of the totals of the bill's other posted credit notes.</param>
    /// <param name="billLineTotals">The line total of every line of the bill.</param>
    /// <param name="alreadyCreditedByLine">What the other posted credit notes credit for each line of the bill they name.</param>
    public Result CheckWithinBill(
        Money billTotal,
        Money alreadyCredited,
        IReadOnlyDictionary<BillLineId, Money> billLineTotals,
        IReadOnlyDictionary<BillLineId, Money> alreadyCreditedByLine)
    {
        ArgumentNullException.ThrowIfNull(billLineTotals);
        ArgumentNullException.ThrowIfNull(alreadyCreditedByLine);

        if (Total + alreadyCredited > billTotal)
        {
            return BillingErrors.CreditNoteExceedsBillTotal(billTotal, alreadyCredited);
        }

        foreach (var credited in _lines.Where(line => line.BillLineId is not null).OrderBy(line => line.LineNo).GroupBy(line => line.BillLineId!.Value))
        {
            var lineNo = credited.First().LineNo;
            if (!billLineTotals.TryGetValue(credited.Key, out var billLineTotal))
            {
                return BillingErrors.CreditNoteBillLineNotFound(lineNo);
            }

            var before = alreadyCreditedByLine.GetValueOrDefault(credited.Key);
            if (credited.Select(line => line.LineTotal).Sum() + before > billLineTotal)
            {
                return BillingErrors.CreditNoteLineExceedsBillLine(lineNo, billLineTotal, before);
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Posts a credit note that waited for approval, under <paramref name="number"/> (BR-212, BR-606): the checker's
    /// approval is the posting. The credit note is frozen, and its whole total is available credit until the use
    /// case applies it. <paramref name="postedBy"/> is the checker.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="number"/> is not a credit note number of the fiscal year of the credit note date: a bug in the caller.
    /// </exception>
    public Result PostApproved(DocumentNumber number, DateTimeOffset postedAtUtc, UserId? postedBy, DateOnly billDate, BusinessDateWindow window)
    {
        ArgumentNullException.ThrowIfNull(number);

        var allowed = CheckCanApprove(billDate, window);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        if (number.Type != DocumentType.CreditNote || number.FiscalYear != Common.FiscalYear.FromDate(CreditNoteDate))
        {
            throw new ArgumentException("A credit note is posted under a credit note number of the fiscal year of its date (BR-212).", nameof(number));
        }

        Number = number.Value;
        FiscalYear = number.FiscalYear;
        SequenceNumber = number.Sequence;
        PostedAtUtc = postedAtUtc;
        PostedByUserId = postedBy;
        AvailableAmount = Total;
        Status = CreditNoteStatus.Posted;
        AddDomainEvent(new CreditNotePosted(Id, number.Value, BillId, PayerId, Total, CreditNoteDate));

        return Result.Success();
    }

    /// <summary>The checker rejected the credit note (BR-605): it is a draft again and can be corrected, voided or submitted once more.</summary>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank or too long.</exception>
    public Result ReturnToDraft(string reason)
    {
        var why = Field.Required(reason, ReasonMaxLength);

        if (RefuseUnlessPending() is { } refused)
        {
            return refused;
        }

        Status = CreditNoteStatus.Draft;
        AddDomainEvent(new CreditNoteReturnedToDraft(Id, why));

        return Result.Success();
    }

    /// <summary>
    /// Whether <paramref name="amount"/> of credit can still be applied (BR-213): the credit note is posted and has at
    /// least that much available. The use case asks for the sum of a request before it changes anything.
    /// </summary>
    public Result CheckCanApply(Money amount)
    {
        if (Status != CreditNoteStatus.Posted || Number is null)
        {
            return BillingErrors.CreditNoteNotPosted();
        }

        return amount > AvailableAmount ? BillingErrors.CreditExceedsAvailable(Number, AvailableAmount) : Result.Success();
    }

    /// <summary>
    /// Applies <paramref name="amount"/> of the credit to bill <paramref name="billId"/> (BR-212, BR-213): the
    /// available amount falls by it and the application is kept. That the bill is posted, belongs to the same payer
    /// and is owed at least the amount is the bill's question, asked in the same use case.
    /// </summary>
    /// <exception cref="ArgumentException">The bill's id is empty or the amount is not above zero: a bug in the caller.</exception>
    public Result<CreditNoteAllocation> Apply(BillId billId, Money amount, DateTimeOffset appliedAtUtc, UserId? appliedBy)
    {
        if (billId.Value == Guid.Empty)
        {
            throw new ArgumentException("Credit is applied to a bill; the empty id is not one.", nameof(billId));
        }

        if (!amount.IsPositive)
        {
            throw new ArgumentException("An application has an amount above zero (BR-213).", nameof(amount));
        }

        var allowed = CheckCanApply(amount);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var allocation = CreditNoteAllocation.Create(billId, amount, appliedAtUtc, appliedBy);
        _allocations.Add(allocation);
        AvailableAmount -= amount;

        // CheckCanApply refused a credit note without its number.
        AddDomainEvent(new CreditNoteApplied(Number ?? string.Empty, billId, amount, AvailableAmount));

        return allocation;
    }

    /// <summary>Records which ledger posting belongs to <paramref name="allocation"/>, once, between the ledger and the save.</summary>
    /// <exception cref="ArgumentException">The application is not one of this credit note, or the posting id is empty.</exception>
    /// <exception cref="InvalidOperationException">The application has its posting already.</exception>
    public void AttachApplicationPosting(CreditNoteAllocation allocation, LedgerPostingId postingId)
    {
        ArgumentNullException.ThrowIfNull(allocation);

        if (!_allocations.Contains(allocation))
        {
            throw new ArgumentException("The application does not belong to this credit note.", nameof(allocation));
        }

        allocation.AttachPosting(postingId);
    }

    /// <summary>Whether the credit note can still be edited: it is a draft. For a use case that wants to know before it does other work.</summary>
    public Result CheckIsDraft() => RefuseUnlessDraft() is { } refused ? refused : Result.Success();

    // What a clerk enters for each line, in the order of the line numbers; the amounts follow from it (BR-202).
    private List<(BillLineId?, string, Quantity, UnitPrice, Money, Percent)> LineContents()
        => [.. _lines.OrderBy(line => line.LineNo).Select(line => (line.BillLineId, line.Description, line.Quantity, line.UnitPrice, line.DiscountAmount, line.TaxRate))];

    // Only a draft changes; every other state refuses with its own answer.
    private Error? RefuseUnlessDraft() => Status switch
    {
        CreditNoteStatus.Draft => null,
        CreditNoteStatus.Voided => BillingErrors.CreditNoteNotDraft(),
        CreditNoteStatus.PendingApproval => BillingErrors.CreditNotePendingApproval(),
        _ => BillingErrors.CreditNoteAlreadyPosted(),
    };

    // Only a credit note that waits is approved or rejected.
    private Error? RefuseUnlessPending() => Status switch
    {
        CreditNoteStatus.PendingApproval => null,
        CreditNoteStatus.Draft => BillingErrors.CreditNoteNotPendingApproval(),
        CreditNoteStatus.Voided => BillingErrors.CreditNoteNotDraft(),
        _ => BillingErrors.CreditNoteAlreadyPosted(),
    };

    private static Error? CheckDate(DateOnly creditNoteDate, DateOnly billDate, BusinessDateWindow window)
    {
        if (creditNoteDate > window.Latest)
        {
            return BillingErrors.CreditNoteDateInFuture();
        }

        if (creditNoteDate < window.Earliest)
        {
            return BillingErrors.CreditNoteDateTooOld(window.Earliest);
        }

        return creditNoteDate < billDate ? BillingErrors.CreditNoteDateBeforeBillDate(billDate) : null;
    }

    // Every line is checked before any is kept, so lines that are refused leave the credit note as it was.
    private static Result<LineAmounts[]> CheckLines(IReadOnlyList<CreditNoteLineDetails> lines)
    {
        if (lines.Count > MaxLines)
        {
            throw new ArgumentException($"A credit note has at most {MaxLines} lines; the validator of the use case should have refused more.", nameof(lines));
        }

        var amounts = new LineAmounts[lines.Count];
        for (var index = 0; index < lines.Count; index++)
        {
            var checkedLine = CreditNoteLine.Check(index + 1, lines[index]);
            if (checkedLine.IsFailure)
            {
                return checkedLine.Error;
            }

            amounts[index] = checkedLine.Value;
        }

        return amounts;
    }

    // A line that keeps its position keeps its row and takes the new content; lines beyond the new count are removed
    // and new positions get new lines, as on a bill.
    private void SetLines(IReadOnlyList<CreditNoteLineDetails> lines, LineAmounts[] amounts)
    {
        _lines.Sort((left, right) => left.LineNo.CompareTo(right.LineNo));

        for (var index = 0; index < lines.Count; index++)
        {
            if (index < _lines.Count)
            {
                _lines[index].Apply(lines[index], amounts[index]);
            }
            else
            {
                _lines.Add(CreditNoteLine.Create(index + 1, lines[index], amounts[index]));
            }
        }

        if (_lines.Count > lines.Count)
        {
            _lines.RemoveRange(lines.Count, _lines.Count - lines.Count);
        }

        // Totals are sums of lines that are already rounded; they are never rounded again (BR-203).
        SubTotal = _lines.Select(line => line.LineAmount).Sum();
        DiscountTotal = _lines.Select(line => line.DiscountAmount).Sum();
        TaxTotal = _lines.Select(line => line.TaxAmount).Sum();
        Total = SubTotal - DiscountTotal + TaxTotal;
        AvailableAmount = Total;
    }
}
