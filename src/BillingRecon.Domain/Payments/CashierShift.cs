using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>
/// A cashier's shift at the cash counter (BR-310 to BR-315; ADR 0060): opened with a float, it takes the cash
/// payments its cashier records and the cash refunds its cashier drafted, and is closed with a count by denomination.
/// <para>
/// The shift holds no money of its own. What it should hold is the float and the Cash entries of the ledger postings
/// that name it (BR-312), which the closing use case reads from the ledger and hands to <see cref="Close"/>. A
/// difference between that and the count is recorded here with its reason and is never posted (BR-314); a closing
/// with a difference waits for a second person, and a closed shift never changes again (BR-315).
/// </para>
/// <para>
/// Every cash document that joins the shift changes <see cref="CashDocuments"/>, and so the shift's row version: a
/// closing that read the ledger before that document was committed loses to it, and a document that read the shift
/// open before a closing was committed loses to the closing (BR-804). That count is not a state change of its own
/// and raises no event: the document's own audit record says what happened.
/// </para>
/// </summary>
public sealed class CashierShift : AggregateRoot<CashierShiftId>, ITenantScoped, ICreationAudited, IModificationAudited, IHasRowVersion
{
    /// <summary>Longest allowed <see cref="DifferenceReason"/>.</summary>
    public const int ReasonMaxLength = 500;

    // Entity Framework creates a shift with this constructor and then sets the properties.
    private CashierShift()
    {
        RowVersion = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Whose shift it is: the user who opened it.</summary>
    public UserId CashierUserId { get; private set; }

    /// <summary>The business date of the shift, in India: the date of its opening, and of every cash document in it (BR-311).</summary>
    public DateOnly BusinessDate { get; private set; }

    /// <summary>Where the shift stands.</summary>
    public CashierShiftStatus Status { get; private set; }

    /// <summary>The cash the drawer held when the shift was opened. Zero or more.</summary>
    public Money OpeningFloat { get; private set; }

    /// <summary>When the shift was opened.</summary>
    public DateTimeOffset OpenedAtUtc { get; private set; }

    /// <summary>How many cash documents joined the shift. It only grows.</summary>
    public int CashDocuments { get; private set; }

    /// <summary>The count by denomination; null until the shift is counted.</summary>
    public CashCount? Denominations { get; private set; }

    /// <summary>The counted cash below one rupee; null until the shift is counted.</summary>
    public Money? LooseChange { get; private set; }

    /// <summary>What the count comes to; null until the shift is counted.</summary>
    public Money? CountedCash { get; private set; }

    /// <summary>What the drawer should hold: the float and the shift's Cash entries (BR-312); null until the shift is counted.</summary>
    public Money? ExpectedCash { get; private set; }

    /// <summary>Counted less expected: above zero an excess, below zero a shortage; null until the shift is counted.</summary>
    public Money? Difference { get; private set; }

    /// <summary>Why the count differs; null when it does not, and until the shift is counted.</summary>
    public string? DifferenceReason { get; private set; }

    /// <summary>When the shift was counted; null until then.</summary>
    public DateTimeOffset? CountedAtUtc { get; private set; }

    /// <summary>When the shift was closed for good; null until then.</summary>
    public DateTimeOffset? ClosedAtUtc { get; private set; }

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

    /// <summary>A shift of <paramref name="cashier"/>, open from <paramref name="openedAtUtc"/> with <paramref name="openingFloat"/>.</summary>
    /// <exception cref="ArgumentException">The cashier is empty or the float is below zero: bugs in the caller, whose validator refuses them first.</exception>
    public static CashierShift Open(UserId cashier, DateOnly businessDate, Money openingFloat, DateTimeOffset openedAtUtc)
    {
        if (cashier.Value == Guid.Empty)
        {
            throw new ArgumentException("A shift belongs to a user; the empty id is not one.", nameof(cashier));
        }

        if (openingFloat < Money.Zero)
        {
            throw new ArgumentException("The float of a shift is zero or more.", nameof(openingFloat));
        }

        var shift = new CashierShift
        {
            CashierUserId = cashier,
            BusinessDate = businessDate,
            Status = CashierShiftStatus.Open,
            OpeningFloat = openingFloat,
            OpenedAtUtc = openedAtUtc,
        };

        // The id is still empty here: it is assigned when the shift is added to the unit of work.
        shift.AddDomainEvent(new CashierShiftOpened(cashier, businessDate, openingFloat));

        return shift;
    }

    /// <summary>
    /// Whether a cash document dated <paramref name="documentDate"/> may join the shift (BR-311): the shift is open and
    /// the date is the shift's business date. <c>shift_not_open</c> or <c>shift_date_mismatch</c> otherwise.
    /// </summary>
    public Result CheckTakes(DateOnly documentDate)
    {
        if (Status != CashierShiftStatus.Open)
        {
            return PaymentsErrors.ShiftNotOpen();
        }

        return documentDate == BusinessDate ? Result.Success() : PaymentsErrors.ShiftDateMismatch(BusinessDate);
    }

    /// <summary>
    /// True when a cash document dated <paramref name="documentDate"/> can join the shift: what <see cref="CheckTakes"/>
    /// asks, for the caller that has another place for a document the shift does not take.
    /// </summary>
    public bool Takes(DateOnly documentDate) => CheckTakes(documentDate).IsSuccess;

    /// <summary>A cash document joined the shift: its ledger posting names it. Changes the shift's row version; see the type's remarks.</summary>
    /// <exception cref="InvalidOperationException">The shift is not open: the caller did not ask <see cref="CheckTakes"/>.</exception>
    public void TakeCashDocument()
    {
        if (Status != CashierShiftStatus.Open)
        {
            throw new InvalidOperationException("Only an open shift takes a cash document (BR-311).");
        }

        CashDocuments++;
    }

    /// <summary>
    /// The cashier counted the drawer (BR-313). <paramref name="cashMovement"/> is the shift's Cash entries in the
    /// ledger, debits less credits, so the expected cash is the float and that (BR-312). With no difference the shift
    /// is closed; with one it needs <paramref name="reason"/> and waits for a checker (BR-314).
    /// </summary>
    /// <returns>
    /// Success; <c>shift_not_open</c> when the shift is counted or closed already; <c>shift_difference_needs_reason</c>
    /// when the count differs and no reason is given.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The loose change is below zero or a rupee or more, or the reason is too long: bugs in the caller, whose validator
    /// refuses them first.
    /// </exception>
    public Result Close(CashCount count, Money looseChange, Money cashMovement, string? reason, DateTimeOffset countedAtUtc)
    {
        if (looseChange < Money.Zero || looseChange >= Money.Of(1m))
        {
            throw new ArgumentException("The loose change of a count is what is below one rupee.", nameof(looseChange));
        }

        var why = Field.Optional(reason, ReasonMaxLength);

        if (Status != CashierShiftStatus.Open)
        {
            return PaymentsErrors.ShiftNotOpen();
        }

        var expected = OpeningFloat + cashMovement;
        var counted = count.Total() + looseChange;
        var difference = counted - expected;

        if (difference != Money.Zero && why is null)
        {
            return PaymentsErrors.ShiftDifferenceNeedsReason(difference);
        }

        Denominations = count;
        LooseChange = looseChange;
        CountedCash = counted;
        ExpectedCash = expected;
        Difference = difference;
        CountedAtUtc = countedAtUtc;

        if (difference == Money.Zero)
        {
            // A reason given for no difference is not kept: there is nothing it explains.
            DifferenceReason = null;
            Status = CashierShiftStatus.Closed;
            ClosedAtUtc = countedAtUtc;
            AddDomainEvent(new CashierShiftClosed(Id, expected, counted));
        }
        else
        {
            DifferenceReason = why;
            Status = CashierShiftStatus.PendingApproval;
            AddDomainEvent(new CashierShiftSubmittedForApproval(Id, expected, counted, difference, why!));
        }

        return Result.Success();
    }

    /// <summary>Succeeds while the closing waits for a checker; <c>shift_not_pending_approval</c> otherwise.</summary>
    public Result CheckAwaitsApproval()
        => Status == CashierShiftStatus.PendingApproval ? Result.Success() : PaymentsErrors.ShiftNotPendingApproval();

    /// <summary>A checker approved the closing with its difference: the shift is closed (BR-314, BR-315).</summary>
    public Result ApproveClosing(DateTimeOffset decidedAtUtc)
    {
        var waits = CheckAwaitsApproval();
        if (waits.IsFailure)
        {
            return waits;
        }

        Status = CashierShiftStatus.Closed;
        ClosedAtUtc = decidedAtUtc;
        AddDomainEvent(new CashierShiftClosingApproved(Id, Difference ?? Money.Zero));

        return Result.Success();
    }

    /// <summary>
    /// A checker rejected the closing, saying why (BR-605): the shift is open again and its count is gone, to be
    /// counted again. The audit record of the rejection keeps the count that was refused.
    /// </summary>
    /// <exception cref="ArgumentException">The reason is blank or longer than an approval request's comment.</exception>
    public Result RejectClosing(string reason)
    {
        var why = Field.Required(reason, ApprovalRequest.CommentMaxLength);

        var waits = CheckAwaitsApproval();
        if (waits.IsFailure)
        {
            return waits;
        }

        Status = CashierShiftStatus.Open;
        Denominations = null;
        LooseChange = null;
        CountedCash = null;
        ExpectedCash = null;
        Difference = null;
        DifferenceReason = null;
        CountedAtUtc = null;
        AddDomainEvent(new CashierShiftClosingRejected(Id, why));

        return Result.Success();
    }
}
