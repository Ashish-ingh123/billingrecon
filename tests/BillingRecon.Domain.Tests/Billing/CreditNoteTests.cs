using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Billing;

/// <summary>
/// The credit note aggregate (BR-210 to BR-214, BR-604 to BR-606): the arithmetic of its lines, its dates, its life
/// from draft through a second person's approval to posted, the caps against its bill, and the credit it holds and
/// applies.
/// </summary>
public sealed class CreditNoteTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly DateOnly BillDate = new(2026, 11, 18);
    private static readonly BusinessDateWindow Window = new(Today, maxBackdateDays: 7);
    private static readonly BillId Bill = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3310"));
    private static readonly BillId OtherBill = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3311"));
    private static readonly PayerId Payer = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3302"));
    private static readonly PatientId Patient = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301"));
    private static readonly BillLineId BillLine = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3320"));
    private static readonly UserId Checker = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3330"));
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 9, 15, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<BillLineId, Money> NoLines = new Dictionary<BillLineId, Money>();

    [Fact]
    public void A_draft_has_the_arithmetic_of_bill_lines_no_number_and_its_total_as_the_amount_it_would_make_available()
    {
        // 3 x 33.335 = 100.005 -> 100.01; less 0.01; tax 18% of 100.00 = 18.00.
        var creditNote = CreditNote.Create(Bill, Payer, Patient, BillDate, Today, "  Overcharged  ",
            [Line(quantity: 3m, unitPrice: 33.335m, taxRate: 18m, discount: 0.01m), Line(quantity: 1m, unitPrice: 10m, taxRate: 0m)], Window).Value;

        creditNote.Status.ShouldBe(CreditNoteStatus.Draft);
        creditNote.Reason.ShouldBe("Overcharged");
        (creditNote.BillId, creditNote.PayerId, creditNote.PatientId).ShouldBe((Bill, Payer, Patient));
        creditNote.Lines.Select(line => (line.LineNo, line.LineAmount, line.TaxAmount, line.LineTotal)).ShouldBe(
        [
            (1, Money.Of(100.01m), Money.Of(18.00m), Money.Of(118.00m)),
            (2, Money.Of(10m), Money.Zero, Money.Of(10m)),
        ]);
        (creditNote.SubTotal, creditNote.DiscountTotal, creditNote.TaxTotal, creditNote.Total).ShouldBe((Money.Of(110.01m), Money.Of(0.01m), Money.Of(18m), Money.Of(128m)));
        creditNote.AvailableAmount.ShouldBe(creditNote.Total);
        creditNote.Number.ShouldBeNull();
        creditNote.Allocations.ShouldBeEmpty();
        creditNote.DomainEvents.ShouldHaveSingleItem().ShouldBe(new CreditNoteDrafted(Bill, Payer, Today, Money.Of(128m)));
    }

    [Fact]
    public void BR205_BR214_the_date_is_not_in_the_future_not_too_old_and_not_before_the_bill_date()
    {
        CreditNote.Create(Bill, Payer, Patient, BillDate, Today.AddDays(1), "x", [Line()], Window).Error.Code.ShouldBe(BillingErrors.CreditNoteDateInFutureCode);
        CreditNote.Create(Bill, Payer, Patient, Today.AddDays(-30), Today.AddDays(-8), "x", [Line()], Window).Error.Code.ShouldBe(BillingErrors.CreditNoteDateTooOldCode);
        CreditNote.Create(Bill, Payer, Patient, BillDate, BillDate.AddDays(-1), "x", [Line()], Window).Error.Code.ShouldBe(BillingErrors.CreditNoteDateBeforeBillDateCode);
        CreditNote.Create(Bill, Payer, Patient, BillDate, BillDate, "x", [Line()], Window).IsSuccess.ShouldBeTrue("the bill date itself is allowed");

        // A draft that is corrected to a date before the bill is refused and stays as it was.
        var creditNote = Draft();
        creditNote.UpdateHeader(BillDate.AddDays(-1), "x", BillDate, Window).Error.Code.ShouldBe(BillingErrors.CreditNoteDateBeforeBillDateCode);
        creditNote.CreditNoteDate.ShouldBe(Today);
    }

    [Fact]
    public void A_line_whose_discount_is_more_than_its_amount_is_refused_and_a_blank_reason_is_a_bug()
    {
        CreditNote.Create(Bill, Payer, Patient, BillDate, Today, "x", [Line(quantity: 1m, unitPrice: 10m, discount: 10.01m)], Window)
            .Error.Code.ShouldBe(BillingErrors.CreditNoteLineDiscountExceedsAmountCode);
        Should.Throw<ArgumentException>(() => CreditNote.Create(Bill, Payer, Patient, BillDate, Today, " ", [Line()], Window));
        Should.Throw<ArgumentException>(() => CreditNote.Create(default, Payer, Patient, BillDate, Today, "x", [Line()], Window));
    }

    [Fact]
    public void A_draft_can_be_corrected_and_voided_and_raises_an_event_only_when_something_changed()
    {
        var creditNote = Draft();
        creditNote.ClearDomainEvents();

        creditNote.ReplaceLines([Line(quantity: 2m, unitPrice: 60m, taxRate: 5m)]).IsSuccess.ShouldBeTrue();
        creditNote.DomainEvents.ShouldBeEmpty("the same lines again change nothing");
        creditNote.UpdateHeader(Today, "Dressing not done", BillDate, Window).IsSuccess.ShouldBeTrue();
        creditNote.DomainEvents.ShouldBeEmpty("the same header again changes nothing");

        creditNote.ReplaceLines([Line(quantity: 1m, unitPrice: 60m, taxRate: 5m), Line(quantity: 1m, unitPrice: 1m, taxRate: 0m)]).IsSuccess.ShouldBeTrue();
        creditNote.Total.ShouldBe(Money.Of(64m));
        creditNote.AvailableAmount.ShouldBe(Money.Of(64m));
        creditNote.UpdateHeader(BillDate, "Billed twice", BillDate, Window).IsSuccess.ShouldBeTrue();
        creditNote.DomainEvents.Select(raised => raised.GetType()).ShouldBe([typeof(CreditNoteLinesReplaced), typeof(CreditNoteHeaderUpdated)]);

        creditNote.Void("Wrong bill", Now, Checker).IsSuccess.ShouldBeTrue();
        creditNote.Status.ShouldBe(CreditNoteStatus.Voided);
        (creditNote.VoidReason, creditNote.VoidedAtUtc, creditNote.VoidedByUserId).ShouldBe(("Wrong bill", Now, Checker));
        creditNote.ReplaceLines([]).Error.Code.ShouldBe(BillingErrors.CreditNoteNotDraftCode);
        creditNote.SubmitForApproval(BillDate, Window).Error.Code.ShouldBe(BillingErrors.CreditNoteNotDraftCode);
    }

    [Fact]
    public void BR212_only_a_draft_with_a_line_and_a_total_above_zero_is_handed_to_a_second_person()
    {
        var empty = CreditNote.Create(Bill, Payer, Patient, BillDate, Today, "x", [], Window).Value;
        empty.SubmitForApproval(BillDate, Window).Error.Code.ShouldBe(BillingErrors.CreditNoteHasNoLinesCode);

        var worthless = CreditNote.Create(Bill, Payer, Patient, BillDate, Today, "x", [Line(quantity: 1m, unitPrice: 0m, taxRate: 0m)], Window).Value;
        worthless.SubmitForApproval(BillDate, Window).Error.Code.ShouldBe(BillingErrors.CreditNoteTotalZeroCode);

        // A draft that has grown old is checked again when it is submitted (BR-205).
        var old = Draft();
        old.SubmitForApproval(BillDate, new BusinessDateWindow(Today.AddDays(30), maxBackdateDays: 7)).Error.Code.ShouldBe(BillingErrors.CreditNoteDateTooOldCode);
        old.Status.ShouldBe(CreditNoteStatus.Draft);

        var creditNote = Draft();
        creditNote.ClearDomainEvents();
        creditNote.SubmitForApproval(BillDate, Window).IsSuccess.ShouldBeTrue();
        creditNote.Status.ShouldBe(CreditNoteStatus.PendingApproval);
        creditNote.Number.ShouldBeNull();
        creditNote.DomainEvents.ShouldHaveSingleItem().ShouldBe(new CreditNoteSubmittedForApproval(creditNote.Id, Bill, Money.Of(126m)));

        // Locked while it waits.
        creditNote.ReplaceLines([]).Error.Code.ShouldBe(BillingErrors.CreditNotePendingApprovalCode);
        creditNote.Void("x", Now, Checker).Error.Code.ShouldBe(BillingErrors.CreditNotePendingApprovalCode);
        creditNote.SubmitForApproval(BillDate, Window).Error.Code.ShouldBe(BillingErrors.CreditNotePendingApprovalCode);
    }

    [Fact]
    public void BR606_the_approval_posts_the_credit_note_under_its_number_and_makes_its_whole_total_available()
    {
        var creditNote = Pending();
        creditNote.ClearDomainEvents();

        creditNote.PostApproved(Number(7), Now, Checker, BillDate, Window).IsSuccess.ShouldBeTrue();

        creditNote.Status.ShouldBe(CreditNoteStatus.Posted);
        (creditNote.Number, creditNote.SequenceNumber, creditNote.FiscalYear).ShouldBe(("CN/26-27/000007", 7, FiscalYear.FromDate(Today)));
        (creditNote.PostedAtUtc, creditNote.PostedByUserId).ShouldBe((Now, Checker));
        creditNote.AvailableAmount.ShouldBe(Money.Of(126m));
        creditNote.DomainEvents.ShouldHaveSingleItem().ShouldBe(new CreditNotePosted(creditNote.Id, "CN/26-27/000007", Bill, Payer, Money.Of(126m), Today));

        // Frozen (BR-006).
        creditNote.ReplaceLines([]).Error.Code.ShouldBe(BillingErrors.CreditNoteAlreadyPostedCode);
        creditNote.ReturnToDraft("x").Error.Code.ShouldBe(BillingErrors.CreditNoteAlreadyPostedCode);
        creditNote.PostApproved(Number(8), Now, Checker, BillDate, Window).Error.Code.ShouldBe(BillingErrors.CreditNoteAlreadyPostedCode);
    }

    [Fact]
    public void A_draft_cannot_be_approved_and_a_number_of_another_document_or_year_is_a_bug()
    {
        Draft().PostApproved(Number(1), Now, Checker, BillDate, Window).Error.Code.ShouldBe(BillingErrors.CreditNoteNotPendingApprovalCode);
        Draft().ReturnToDraft("x").Error.Code.ShouldBe(BillingErrors.CreditNoteNotPendingApprovalCode);

        var billNumber = DocumentNumber.Create(DocumentType.Bill, FiscalYear.FromDate(Today), "BIL", 1);
        var lastYear = DocumentNumber.Create(DocumentType.CreditNote, FiscalYear.FromDate(Today.AddYears(-1)), "CN", 1);
        Should.Throw<ArgumentException>(() => Pending().PostApproved(billNumber, Now, Checker, BillDate, Window));
        Should.Throw<ArgumentException>(() => Pending().PostApproved(lastYear, Now, Checker, BillDate, Window));
    }

    [Fact]
    public void BR605_a_rejection_makes_the_credit_note_a_draft_again()
    {
        var creditNote = Pending();
        creditNote.ClearDomainEvents();

        creditNote.ReturnToDraft("Only one dressing").IsSuccess.ShouldBeTrue();

        creditNote.Status.ShouldBe(CreditNoteStatus.Draft);
        creditNote.DomainEvents.ShouldHaveSingleItem().ShouldBe(new CreditNoteReturnedToDraft(creditNote.Id, "Only one dressing"));
        creditNote.ReplaceLines([Line(quantity: 1m)]).IsSuccess.ShouldBeTrue("a draft can be corrected");
    }

    [Fact]
    public void BR210_the_credits_of_a_bill_with_this_one_do_not_exceed_its_total()
    {
        var creditNote = Draft();

        creditNote.CheckWithinBill(Money.Of(716m), Money.Of(590m), NoLines, NoLines).IsSuccess.ShouldBeTrue("126 + 590 is exactly the bill");
        creditNote.CheckWithinBill(Money.Of(716m), Money.Of(590.01m), NoLines, NoLines).Error.Code.ShouldBe(BillingErrors.CreditNoteExceedsBillTotalCode);
        creditNote.CheckWithinBill(Money.Of(125.99m), Money.Zero, NoLines, NoLines).Error.Code.ShouldBe(BillingErrors.CreditNoteExceedsBillTotalCode);
    }

    [Fact]
    public void BR210_what_adjustments_took_off_the_bill_counts_against_the_cap_like_a_credit()
    {
        // Review 2026-11, R1. This credit note is 126.00.
        var creditNote = Draft();

        // A bill of 716.00, 590.00 of it written off: exactly 126.00 is left to credit.
        creditNote.CheckWithinBill(Money.Of(716m), Money.Zero, NoLines, NoLines, Money.Of(590m)).IsSuccess.ShouldBeTrue();
        var refused = creditNote.CheckWithinBill(Money.Of(716m), Money.Zero, NoLines, NoLines, Money.Of(590.01m));
        refused.Error.Code.ShouldBe(BillingErrors.CreditNoteExceedsBillTotalCode);
        refused.Error.Message.ShouldContain("taken off by adjustments");

        // Credits and adjustments together: 400 credited and 190 adjusted leave 126; one paisa more of either does not.
        creditNote.CheckWithinBill(Money.Of(716m), Money.Of(400m), NoLines, NoLines, Money.Of(190m)).IsSuccess.ShouldBeTrue();
        creditNote.CheckWithinBill(Money.Of(716m), Money.Of(400.01m), NoLines, NoLines, Money.Of(190m)).IsFailure.ShouldBeTrue();

        // A bill wholly written off can be credited nothing.
        creditNote.CheckWithinBill(Money.Of(126m), Money.Zero, NoLines, NoLines, Money.Of(126m)).IsFailure.ShouldBeTrue();

        // With nothing adjusted the answer and its words are what they were.
        creditNote.CheckWithinBill(Money.Of(125.99m), Money.Zero, NoLines, NoLines, Money.Zero).Error.Message.ShouldNotContain("adjustments");
    }

    [Fact]
    public void BR211_the_credits_of_one_line_of_the_bill_with_these_lines_do_not_exceed_that_line()
    {
        // Two lines of 63.00 each for the same line of the bill, whose total is 126.00.
        var creditNote = CreditNote.Create(Bill, Payer, Patient, BillDate, Today, "x",
            [Line(quantity: 1m, billLine: BillLine), Line(quantity: 1m, billLine: BillLine), Line(quantity: 1m, unitPrice: 5m, taxRate: 0m)], Window).Value;
        var billLines = new Dictionary<BillLineId, Money> { [BillLine] = Money.Of(126m) };

        creditNote.CheckWithinBill(Money.Of(716m), Money.Zero, billLines, NoLines).IsSuccess.ShouldBeTrue("exactly the line");
        creditNote.CheckWithinBill(Money.Of(716m), Money.Zero, billLines, new Dictionary<BillLineId, Money> { [BillLine] = Money.Of(0.01m) })
            .Error.Code.ShouldBe(BillingErrors.CreditNoteLineExceedsBillLineCode);
        creditNote.CheckWithinBill(Money.Of(716m), Money.Zero, NoLines, NoLines).Error.Code.ShouldBe(BillingErrors.CreditNoteBillLineNotFoundCode);
    }

    [Fact]
    public void BR213_credit_is_applied_only_from_a_posted_credit_note_and_never_beyond_what_it_has_available()
    {
        Draft().CheckCanApply(Money.Of(1m)).Error.Code.ShouldBe(BillingErrors.CreditNoteNotPostedCode);
        Pending().Apply(Bill, Money.Of(1m), Now, Checker).Error.Code.ShouldBe(BillingErrors.CreditNoteNotPostedCode);

        var creditNote = Posted();
        creditNote.ClearDomainEvents();

        var first = creditNote.Apply(Bill, Money.Of(26m), Now, Checker).Value;
        creditNote.AvailableAmount.ShouldBe(Money.Of(100m));
        (first.BillId, first.Amount, first.Status, first.AllocatedAtUtc, first.AllocatedByUserId).ShouldBe((Bill, Money.Of(26m), CreditNoteAllocationStatus.Active, Now, Checker));
        creditNote.DomainEvents.ShouldHaveSingleItem().ShouldBe(new CreditNoteApplied("CN/26-27/000007", Bill, Money.Of(26m), Money.Of(100m)));

        creditNote.Apply(OtherBill, Money.Of(100.01m), Now, null).Error.Code.ShouldBe(BillingErrors.CreditExceedsAvailableCode);
        creditNote.AvailableAmount.ShouldBe(Money.Of(100m), "a refusal changes nothing");
        creditNote.Allocations.Count.ShouldBe(1);

        creditNote.Apply(OtherBill, Money.Of(100m), Now, null).IsSuccess.ShouldBeTrue();
        creditNote.AvailableAmount.ShouldBe(Money.Zero);
        creditNote.Allocations.Count.ShouldBe(2);
        creditNote.Total.ShouldBe(Money.Of(126m), "a posted credit note keeps its total (BR-006)");

        Should.Throw<ArgumentException>(() => creditNote.Apply(Bill, Money.Zero, Now, null));
        Should.Throw<ArgumentException>(() => creditNote.Apply(default, Money.Of(1m), Now, null));
    }

    [Fact]
    public void An_application_takes_its_posting_once_and_only_from_its_own_credit_note()
    {
        var creditNote = Posted();
        var application = creditNote.Apply(Bill, Money.Of(26m), Now, Checker).Value;
        var posting = new LedgerPostingId(Guid.NewGuid());

        Should.Throw<ArgumentException>(() => creditNote.AttachApplicationPosting(application, default));
        Should.Throw<ArgumentException>(() => Posted().AttachApplicationPosting(application, posting));
        creditNote.AttachApplicationPosting(application, posting);
        application.PostingId.ShouldBe(posting);
        Should.Throw<InvalidOperationException>(() => creditNote.AttachApplicationPosting(application, new LedgerPostingId(Guid.NewGuid())));
    }

    [Fact]
    public void BR005_a_credit_note_and_its_applications_are_never_deletable_and_only_a_line_of_a_draft_is()
    {
        typeof(IHardDeletable).IsAssignableFrom(typeof(CreditNote)).ShouldBeFalse();
        typeof(IHardDeletable).IsAssignableFrom(typeof(CreditNoteAllocation)).ShouldBeFalse();
        typeof(IHardDeletable).IsAssignableFrom(typeof(CreditNoteLine)).ShouldBeTrue();
    }

    // 2 x 60.00 at 5%: 126.00.
    private static CreditNoteLineDetails Line(decimal quantity = 2m, decimal unitPrice = 60m, decimal taxRate = 5m, decimal discount = 0m, BillLineId? billLine = null) => new()
    {
        BillLineId = billLine,
        Description = "Dressing",
        Quantity = Quantity.Of(quantity),
        UnitPrice = UnitPrice.Of(unitPrice),
        DiscountAmount = Money.Of(discount),
        TaxRate = Percent.Of(taxRate),
    };

    private static DocumentNumber Number(int sequence) => DocumentNumber.Create(DocumentType.CreditNote, FiscalYear.FromDate(Today), "CN", sequence);

    private static CreditNote Draft() => CreditNote.Create(Bill, Payer, Patient, BillDate, Today, "Dressing not done", [Line()], Window).Value;

    private static CreditNote Pending()
    {
        var creditNote = Draft();
        creditNote.SubmitForApproval(BillDate, Window).IsSuccess.ShouldBeTrue();

        return creditNote;
    }

    private static CreditNote Posted()
    {
        var creditNote = Pending();
        creditNote.PostApproved(Number(7), Now, Checker, BillDate, Window).IsSuccess.ShouldBeTrue();

        return creditNote;
    }
}
