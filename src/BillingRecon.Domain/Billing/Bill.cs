using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// A bill: raised for one patient and owed by one payer (BR-106), with lines whose arithmetic is BR-202 and totals
/// that are the sums of those lines (BR-203). So far a bill is a draft, which can be edited and voided, or a voided
/// draft, which can no longer change (BR-204); it has no number and no ledger effect. Posting, with its number, its
/// ledger entries and the states that follow the balance, arrives with the posting slices
/// (docs/domain-model.md, sections 4.4 and 5.1).
/// <para>
/// The patient and the payer are fixed when the draft is created; whether they exist and are active is the use case's
/// question, answered through the Payers module's contracts (BR-102, BR-105). The dates follow BR-205, with today and
/// the tenant's back-dating limit handed in as a <see cref="BusinessDateWindow"/>. <see cref="BalanceDue"/> is the
/// cache of what the bill is owed (BR-405); on a draft it simply equals <see cref="Total"/>.
/// </para>
/// </summary>
public sealed class Bill : AggregateRoot<BillId>, ITenantScoped, ICreationAudited, IModificationAudited, IHasRowVersion
{
    /// <summary>The most lines a bill may have.</summary>
    public const int MaxLines = 200;

    /// <summary>Longest allowed <see cref="EncounterReference"/>.</summary>
    public const int EncounterReferenceMaxLength = 50;

    /// <summary>Longest allowed <see cref="Notes"/>.</summary>
    public const int NotesMaxLength = 1000;

    /// <summary>Longest allowed <see cref="VoidReason"/>.</summary>
    public const int VoidReasonMaxLength = 500;

    private readonly List<BillLine> _lines = [];

    // Also the constructor Entity Framework uses to load a bill: it passes the two stored ids and then sets the
    // remaining properties and fills the lines.
    private Bill(PatientId patientId, PayerId payerId)
    {
        PatientId = patientId;
        PayerId = payerId;
        Status = BillStatus.Draft;
        RowVersion = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The patient the bill is for (BR-106). It never changes.</summary>
    public PatientId PatientId { get; private set; }

    /// <summary>The payer who owes the bill (BR-106). It never changes.</summary>
    public PayerId PayerId { get; private set; }

    /// <summary>The business date of the bill, a calendar date in India (BR-003, BR-205).</summary>
    public DateOnly BillDate { get; private set; }

    /// <summary>The date the bill is due, not before <see cref="BillDate"/> (BR-205).</summary>
    public DateOnly DueDate { get; private set; }

    /// <summary>The visit or admission the bill is for, as the hospital's own system names it.</summary>
    public string? EncounterReference { get; private set; }

    /// <summary>A note for whoever reads the bill.</summary>
    public string? Notes { get; private set; }

    /// <summary>Where the bill is in its life.</summary>
    public BillStatus Status { get; private set; }

    /// <summary>The sum of the line amounts (BR-203).</summary>
    public Money SubTotal { get; private set; }

    /// <summary>The sum of the line discounts (BR-203).</summary>
    public Money DiscountTotal { get; private set; }

    /// <summary>The sum of the line taxes (BR-203).</summary>
    public Money TaxTotal { get; private set; }

    /// <summary>What the bill comes to: <c>SubTotal - DiscountTotal + TaxTotal</c>, never negative (BR-203).</summary>
    public Money Total { get; private set; }

    /// <summary>What is still owed on the bill (BR-405). A draft is owed nothing yet and carries its total here.</summary>
    public Money BalanceDue { get; private set; }

    /// <summary>When the draft was voided; null unless it was.</summary>
    public DateTimeOffset? VoidedAtUtc { get; private set; }

    /// <summary>Who voided the draft; null when it was not voided, or no user did it.</summary>
    public UserId? VoidedByUserId { get; private set; }

    /// <summary>Why the draft was voided; null unless it was.</summary>
    public string? VoidReason { get; private set; }

    /// <summary>The gapless number of the bill, as it is printed (BR-801); null until the bill is posted.</summary>
    public string? Number { get; private set; }

    /// <summary>The fiscal year of the bill date, which the number belongs to (BR-004); null until the bill is posted.</summary>
    public FiscalYear? FiscalYear { get; private set; }

    /// <summary>The position of the bill in the series of its fiscal year, from 1 (BR-801); null until the bill is posted.</summary>
    public int? SequenceNumber { get; private set; }

    /// <summary>When the bill was posted; null until it is.</summary>
    public DateTimeOffset? PostedAtUtc { get; private set; }

    /// <summary>Who posted the bill; null until it is posted, or when no user did it.</summary>
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

    /// <summary>The lines of the bill, in the order of their numbers. A draft may have none; a bill needs one to be posted.</summary>
    public IReadOnlyList<BillLine> Lines => _lines;

    /// <summary>
    /// Creates a draft for <paramref name="patientId"/>, owed by <paramref name="payerId"/>, with
    /// <paramref name="lines"/> in the order given. The dates must lie in <paramref name="window"/> (BR-205) and every
    /// line must pass BR-201; the first rule that fails is returned and nothing is created.
    /// </summary>
    /// <exception cref="ArgumentException">An id is empty, or a value breaks a limit the validator checks.</exception>
    public static Result<Bill> Create(
        PatientId patientId,
        PayerId payerId,
        BillHeader header,
        IReadOnlyList<BillLineDetails> lines,
        BusinessDateWindow window)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(lines);

        if (patientId.Value == Guid.Empty)
        {
            throw new ArgumentException("A bill is for a patient; the empty id is not one.", nameof(patientId));
        }

        if (payerId.Value == Guid.Empty)
        {
            throw new ArgumentException("A bill is owed by a payer; the empty id is not one.", nameof(payerId));
        }

        if (CheckDates(header, window) is { } refusedDates)
        {
            return refusedDates;
        }

        var checkedLines = CheckLines(lines);
        if (checkedLines.IsFailure)
        {
            return checkedLines.Error;
        }

        var bill = new Bill(patientId, payerId);
        bill.Apply(header);
        bill.Apply(lines, checkedLines.Value);

        // The id is still empty here: it is assigned when the bill is added to the unit of work.
        bill.AddDomainEvent(new BillDrafted(patientId, payerId, bill.BillDate, bill.Total));

        return bill;
    }

    /// <summary>
    /// Replaces the dates and the two texts of a draft: a text left out is cleared. A bill date that changes must lie
    /// in <paramref name="window"/>; one that stays as it is is not checked again, so a draft that has grown old can
    /// still be corrected, and posting checks the date once more (BR-205). Raises <see cref="BillHeaderUpdated"/> when
    /// a value changed.
    /// </summary>
    /// <exception cref="ArgumentException">A value of <paramref name="header"/> breaks a limit the validator checks.</exception>
    public Result UpdateHeader(BillHeader header, BusinessDateWindow window)
    {
        ArgumentNullException.ThrowIfNull(header);

        if (RefuseUnlessDraft() is { } refused)
        {
            return refused;
        }

        var refusedDates = header.BillDate == BillDate ? CheckDueDate(header) : CheckDates(header, window);
        if (refusedDates is not null)
        {
            return refusedDates;
        }

        var before = (BillDate, DueDate, EncounterReference, Notes);
        Apply(header);

        if (before != (BillDate, DueDate, EncounterReference, Notes))
        {
            AddDomainEvent(new BillHeaderUpdated(Id, BillDate, DueDate));
        }

        return Result.Success();
    }

    /// <summary>
    /// Replaces every line of a draft with <paramref name="lines"/>, in the order given, and recomputes the totals.
    /// Every line must pass BR-201; the first that fails is returned and the bill stays as it was. Raises
    /// <see cref="BillLinesReplaced"/> when a line changed; the same lines again change nothing.
    /// </summary>
    /// <exception cref="ArgumentException">There are more than <see cref="MaxLines"/> lines, or a value breaks a limit the validator checks.</exception>
    public Result ReplaceLines(IReadOnlyList<BillLineDetails> lines)
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
        Apply(lines, checkedLines.Value);

        if (!before.SequenceEqual(LineContents()))
        {
            AddDomainEvent(new BillLinesReplaced(Id, _lines.Count, Total));
        }

        return Result.Success();
    }

    /// <summary>
    /// Voids a draft (BR-204, US-23): it keeps its content and its totals, and can no longer be edited. A bill that is
    /// not a draft is refused.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is blank or longer than <see cref="VoidReasonMaxLength"/>.</exception>
    public Result Void(string reason, DateTimeOffset voidedAtUtc, UserId? voidedBy)
    {
        var why = Field.Required(reason, VoidReasonMaxLength);

        if (RefuseUnlessDraft() is { } refused)
        {
            return refused;
        }

        Status = BillStatus.Voided;
        VoidedAtUtc = voidedAtUtc;
        VoidedByUserId = voidedBy;
        VoidReason = why;
        AddDomainEvent(new BillVoided(Id, PatientId, PayerId, Total, why));

        return Result.Success();
    }

    // What a clerk enters for each line, in the order of the line numbers; the amounts follow from it (BR-202).
    private List<(ServiceItemId?, string, Quantity, UnitPrice, Money, Percent)> LineContents()
        => [.. _lines.OrderBy(line => line.LineNo).Select(line => (line.ServiceItemId, line.Description, line.Quantity, line.UnitPrice, line.DiscountAmount, line.TaxRate))];

    /// <summary>
    /// Whether the bill can be posted today (BR-206): it is a draft, it has a line, and its bill date still lies in
    /// <paramref name="window"/>, which is checked again here because a draft may have grown old (BR-205). The use
    /// case asks before it takes the number, so that a refusal never touches the series (BR-801).
    /// </summary>
    public Result CheckCanPost(BusinessDateWindow window)
    {
        if (RefuseUnlessDraft() is { } refused)
        {
            return refused;
        }

        if (_lines.Count == 0)
        {
            return BillingErrors.BillHasNoLines();
        }

        if (BillDate > window.Latest)
        {
            return BillingErrors.BillDateInFuture();
        }

        return BillDate < window.Earliest ? BillingErrors.BillDateTooOld(window.Earliest) : Result.Success();
    }

    /// <summary>
    /// Posts the draft under <paramref name="number"/> (BR-206, US-21): the bill is frozen (BR-006), the payer owes its
    /// total, and the status follows the balance (BR-208): <see cref="BillStatus.Posted"/>, or
    /// <see cref="BillStatus.Settled"/> at once when the bill comes to nothing. The rules of
    /// <see cref="CheckCanPost"/> are checked again; a refusal leaves the bill a draft.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="number"/> is not a bill number of the fiscal year of the bill date: a bug in the caller.
    /// </exception>
    public Result Post(DocumentNumber number, DateTimeOffset postedAtUtc, UserId? postedBy, BusinessDateWindow window)
    {
        ArgumentNullException.ThrowIfNull(number);

        var allowed = CheckCanPost(window);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        if (number.Type != DocumentType.Bill || number.FiscalYear != Common.FiscalYear.FromDate(BillDate))
        {
            throw new ArgumentException("A bill is posted under a bill number of the fiscal year of its bill date (BR-206).", nameof(number));
        }

        Number = number.Value;
        FiscalYear = number.FiscalYear;
        SequenceNumber = number.Sequence;
        PostedAtUtc = postedAtUtc;
        PostedByUserId = postedBy;
        BalanceDue = Total;
        Status = Total.IsZero ? BillStatus.Settled : BillStatus.Posted;
        AddDomainEvent(new BillPosted(Id, number.Value, PayerId, Total, BillDate));

        return Result.Success();
    }

    /// <summary>
    /// Whether the bill can still be edited: it is a draft. A voided draft answers <c>bill_not_draft</c>, a posted
    /// bill <c>bill_already_posted</c> (BR-204, BR-006). For a use case that wants to know before it does other work.
    /// </summary>
    public Result CheckIsDraft() => RefuseUnlessDraft() is { } refused ? refused : Result.Success();

    // Only a draft changes. A voided draft and a posted bill both refuse, with different answers: a posted bill is
    // frozen for good (BR-006), and the client is told that it is posted, not merely that it is no draft.
    private Error? RefuseUnlessDraft() => Status switch
    {
        BillStatus.Draft => null,
        BillStatus.Voided => BillingErrors.BillNotDraft(),
        _ => BillingErrors.BillAlreadyPosted(),
    };

    private static Error? CheckDates(BillHeader header, BusinessDateWindow window)
    {
        if (header.BillDate > window.Latest)
        {
            return BillingErrors.BillDateInFuture();
        }

        if (header.BillDate < window.Earliest)
        {
            return BillingErrors.BillDateTooOld(window.Earliest);
        }

        return CheckDueDate(header);
    }

    private static Error? CheckDueDate(BillHeader header)
        => header.DueDate < header.BillDate ? BillingErrors.BillDueDateBeforeBillDate() : null;

    // Every line is checked before any is kept, so lines that are refused leave the bill as it was.
    private static Result<LineAmounts[]> CheckLines(IReadOnlyList<BillLineDetails> lines)
    {
        if (lines.Count > MaxLines)
        {
            throw new ArgumentException($"A bill has at most {MaxLines} lines; the validator of the use case should have refused more.", nameof(lines));
        }

        var amounts = new LineAmounts[lines.Count];
        for (var index = 0; index < lines.Count; index++)
        {
            var checkedLine = BillLine.Check(index + 1, lines[index]);
            if (checkedLine.IsFailure)
            {
                return checkedLine.Error;
            }

            amounts[index] = checkedLine.Value;
        }

        return amounts;
    }

    // Both texts are checked before anything is assigned, so a header that is refused leaves the bill as it was.
    private void Apply(BillHeader header)
    {
        var encounterReference = Field.Optional(header.EncounterReference, EncounterReferenceMaxLength);
        var notes = Field.Optional(header.Notes, NotesMaxLength);

        BillDate = header.BillDate;
        DueDate = header.DueDate;
        EncounterReference = encounterReference;
        Notes = notes;
    }

    // A line that keeps its position keeps its row and takes the new content; lines beyond the new count are removed
    // and new positions get new lines. So a line number never moves from one row to another within one save.
    private void Apply(IReadOnlyList<BillLineDetails> lines, LineAmounts[] amounts)
    {
        // The lines come from the database in no promised order; here position and line number must agree.
        _lines.Sort((left, right) => left.LineNo.CompareTo(right.LineNo));

        for (var index = 0; index < lines.Count; index++)
        {
            if (index < _lines.Count)
            {
                _lines[index].Apply(lines[index], amounts[index]);
            }
            else
            {
                _lines.Add(BillLine.Create(index + 1, lines[index], amounts[index]));
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
        BalanceDue = Total;
    }
}
