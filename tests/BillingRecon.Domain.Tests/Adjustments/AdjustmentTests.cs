using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Adjustments;

/// <summary>
/// The adjustment aggregate (BR-501 to BR-503, BR-604 to BR-606): its three kinds, the limit on a rounding, its dates,
/// and its life from draft through a second person's approval to posted.
/// </summary>
public sealed class AdjustmentTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly DateOnly BillDate = new(2026, 11, 18);
    private static readonly BusinessDateWindow Window = new(Today, maxBackdateDays: 7);
    private static readonly BillId Bill = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3410"));
    private static readonly PayerId Payer = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3402"));
    private static readonly UserId Checker = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3430"));
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 9, 15, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(AdjustmentKind.Discount, 250.00)]
    [InlineData(AdjustmentKind.WriteOff, 9999999999.99)]
    [InlineData(AdjustmentKind.Rounding, 0.99)]
    [InlineData(AdjustmentKind.Rounding, 0.01)]
    public void BR501_a_draft_of_each_kind_keeps_what_it_was_given_and_has_no_number(AdjustmentKind kind, decimal amount)
    {
        var adjustment = Adjustment.Create(Bill, Payer, BillDate, Today, kind, Money.Of(amount), "  Agreed with the patient  ", Window).Value;

        adjustment.Status.ShouldBe(AdjustmentStatus.Draft);
        (adjustment.BillId, adjustment.PayerId, adjustment.Kind, adjustment.Amount, adjustment.AdjustmentDate).ShouldBe((Bill, Payer, kind, Money.Of(amount), Today));
        adjustment.Reason.ShouldBe("Agreed with the patient");
        adjustment.Number.ShouldBeNull();
        adjustment.PostingId.ShouldBeNull();
        adjustment.DomainEvents.ShouldHaveSingleItem().ShouldBe(new AdjustmentDrafted(Bill, Payer, kind, Money.Of(amount), Today));
    }

    [Theory]
    [InlineData(1.00)]
    [InlineData(1.01)]
    [InlineData(500.00)]
    public void BR501_a_rounding_of_one_rupee_or_more_is_refused(decimal amount)
    {
        Adjustment.Create(Bill, Payer, BillDate, Today, AdjustmentKind.Rounding, Money.Of(amount), "x", Window)
            .Error.Code.ShouldBe(AdjustmentsErrors.AdjustmentRoundingTooLargeCode);

        // A draft cannot be corrected into one either, and stays as it was.
        var adjustment = Draft(AdjustmentKind.Discount, amount);
        adjustment.Update(Today, AdjustmentKind.Rounding, Money.Of(amount), "x", BillDate, Window).Error.Code.ShouldBe(AdjustmentsErrors.AdjustmentRoundingTooLargeCode);
        adjustment.Kind.ShouldBe(AdjustmentKind.Discount);
    }

    [Fact]
    public void BR205_BR501_the_date_is_not_in_the_future_not_too_old_and_not_before_the_bill_date()
    {
        Create(Today.AddDays(1), BillDate).Error.Code.ShouldBe(AdjustmentsErrors.AdjustmentDateInFutureCode);
        Create(Today.AddDays(-8), Today.AddDays(-30)).Error.Code.ShouldBe(AdjustmentsErrors.AdjustmentDateTooOldCode);
        Create(BillDate.AddDays(-1), BillDate).Error.Code.ShouldBe(AdjustmentsErrors.AdjustmentDateBeforeBillDateCode);
        Create(BillDate, BillDate).IsSuccess.ShouldBeTrue("the bill date itself is allowed");

        static Result<Adjustment> Create(DateOnly date, DateOnly billDate)
            => Adjustment.Create(Bill, Payer, billDate, date, AdjustmentKind.Discount, Money.Of(10m), "x", Window);
    }

    [Fact]
    public void An_amount_of_nothing_a_kind_nobody_defined_a_blank_reason_and_an_empty_id_are_bugs()
    {
        Should.Throw<ArgumentException>(() => Adjustment.Create(Bill, Payer, BillDate, Today, AdjustmentKind.Discount, Money.Zero, "x", Window));
        Should.Throw<ArgumentException>(() => Adjustment.Create(Bill, Payer, BillDate, Today, AdjustmentKind.Discount, Money.Of(10_000_000_000m), "x", Window));
        Should.Throw<ArgumentException>(() => Adjustment.Create(Bill, Payer, BillDate, Today, (AdjustmentKind)99, Money.Of(1m), "x", Window));
        Should.Throw<ArgumentException>(() => Adjustment.Create(Bill, Payer, BillDate, Today, AdjustmentKind.Discount, Money.Of(1m), " ", Window));
        Should.Throw<ArgumentException>(() => Adjustment.Create(default, Payer, BillDate, Today, AdjustmentKind.Discount, Money.Of(1m), "x", Window));
    }

    [Fact]
    public void A_draft_can_be_corrected_and_voided_and_raises_an_event_only_when_something_changed()
    {
        var adjustment = Draft();
        adjustment.ClearDomainEvents();

        adjustment.Update(Today, AdjustmentKind.WriteOff, Money.Of(126m), "Patient cannot pay", BillDate, Window).IsSuccess.ShouldBeTrue();
        adjustment.DomainEvents.ShouldBeEmpty("the same values again change nothing");

        adjustment.Update(BillDate, AdjustmentKind.Discount, Money.Of(100m), "Staff discount", BillDate, Window).IsSuccess.ShouldBeTrue();
        (adjustment.AdjustmentDate, adjustment.Kind, adjustment.Amount, adjustment.Reason).ShouldBe((BillDate, AdjustmentKind.Discount, Money.Of(100m), "Staff discount"));
        adjustment.DomainEvents.ShouldHaveSingleItem().ShouldBe(new AdjustmentUpdated(adjustment.Id, AdjustmentKind.Discount, Money.Of(100m), BillDate));

        adjustment.Void("Raised on the wrong bill", Now, Checker).IsSuccess.ShouldBeTrue();
        adjustment.Status.ShouldBe(AdjustmentStatus.Voided);
        (adjustment.VoidReason, adjustment.VoidedAtUtc, adjustment.VoidedByUserId).ShouldBe(("Raised on the wrong bill", Now, Checker));
        adjustment.Update(Today, AdjustmentKind.Discount, Money.Of(1m), "x", BillDate, Window).Error.Code.ShouldBe(AdjustmentsErrors.AdjustmentNotDraftCode);
        adjustment.SubmitForApproval(BillDate, Window).Error.Code.ShouldBe(AdjustmentsErrors.AdjustmentNotDraftCode);
    }

    [Fact]
    public void BR501_the_amount_fits_what_the_bill_is_owed_or_is_refused()
    {
        var adjustment = Draft();

        adjustment.CheckFits("BIL/26-27/000042", Money.Of(126m)).IsSuccess.ShouldBeTrue("exactly the balance");
        adjustment.CheckFits("BIL/26-27/000042", Money.Of(125.99m)).Error.Code.ShouldBe(AdjustmentsErrors.AdjustmentExceedsBalanceDueCode);
        adjustment.CheckFits("BIL/26-27/000042", Money.Zero).Error.Code.ShouldBe(AdjustmentsErrors.AdjustmentExceedsBalanceDueCode);
    }

    [Fact]
    public void BR502_every_adjustment_waits_for_a_second_person_a_rounding_of_one_paisa_included()
    {
        var rounding = Draft(AdjustmentKind.Rounding, 0.01m);
        rounding.ClearDomainEvents();

        rounding.SubmitForApproval(BillDate, Window).IsSuccess.ShouldBeTrue();

        rounding.Status.ShouldBe(AdjustmentStatus.PendingApproval);
        rounding.Number.ShouldBeNull();
        rounding.DomainEvents.ShouldHaveSingleItem().ShouldBe(new AdjustmentSubmittedForApproval(rounding.Id, Bill, Money.Of(0.01m)));

        // Locked while it waits.
        rounding.Update(Today, AdjustmentKind.Rounding, Money.Of(0.02m), "x", BillDate, Window).Error.Code.ShouldBe(AdjustmentsErrors.AdjustmentPendingApprovalCode);
        rounding.Void("x", Now, Checker).Error.Code.ShouldBe(AdjustmentsErrors.AdjustmentPendingApprovalCode);
        rounding.SubmitForApproval(BillDate, Window).Error.Code.ShouldBe(AdjustmentsErrors.AdjustmentPendingApprovalCode);

        // A draft that has grown old is checked again when it is submitted (BR-205).
        var old = Draft();
        old.SubmitForApproval(BillDate, new BusinessDateWindow(Today.AddDays(30), maxBackdateDays: 7)).Error.Code.ShouldBe(AdjustmentsErrors.AdjustmentDateTooOldCode);
        old.Status.ShouldBe(AdjustmentStatus.Draft);
    }

    [Fact]
    public void BR606_the_approval_posts_the_adjustment_under_its_number_and_freezes_it()
    {
        var adjustment = Pending();
        adjustment.ClearDomainEvents();

        adjustment.PostApproved(Number(3), Now, Checker, BillDate, Window).IsSuccess.ShouldBeTrue();

        adjustment.Status.ShouldBe(AdjustmentStatus.Posted);
        (adjustment.Number, adjustment.SequenceNumber, adjustment.FiscalYear).ShouldBe(("ADJ/26-27/000003", 3, FiscalYear.FromDate(Today)));
        (adjustment.PostedAtUtc, adjustment.PostedByUserId).ShouldBe((Now, Checker));
        adjustment.DomainEvents.ShouldHaveSingleItem().ShouldBe(
            new AdjustmentPosted(adjustment.Id, "ADJ/26-27/000003", Bill, Payer, AdjustmentKind.WriteOff, Money.Of(126m), "Patient cannot pay"));

        // Frozen (BR-006).
        adjustment.Update(Today, AdjustmentKind.Discount, Money.Of(1m), "x", BillDate, Window).Error.Code.ShouldBe(AdjustmentsErrors.AdjustmentAlreadyPostedCode);
        adjustment.Void("x", Now, Checker).Error.Code.ShouldBe(AdjustmentsErrors.AdjustmentAlreadyPostedCode);
        adjustment.ReturnToDraft("x").Error.Code.ShouldBe(AdjustmentsErrors.AdjustmentAlreadyPostedCode);
        adjustment.PostApproved(Number(4), Now, Checker, BillDate, Window).Error.Code.ShouldBe(AdjustmentsErrors.AdjustmentAlreadyPostedCode);
    }

    [Fact]
    public void A_posted_adjustment_takes_its_ledger_posting_once_and_a_draft_takes_none()
    {
        var posting = new LedgerPostingId(Guid.NewGuid());
        Should.Throw<InvalidOperationException>(() => Draft().AttachPosting(posting));

        var adjustment = Pending();
        adjustment.PostApproved(Number(3), Now, Checker, BillDate, Window).IsSuccess.ShouldBeTrue();
        Should.Throw<ArgumentException>(() => adjustment.AttachPosting(default));
        adjustment.AttachPosting(posting);
        adjustment.PostingId.ShouldBe(posting);
        Should.Throw<InvalidOperationException>(() => adjustment.AttachPosting(new LedgerPostingId(Guid.NewGuid())));
    }

    [Fact]
    public void A_draft_cannot_be_approved_and_a_number_of_another_document_or_year_is_a_bug()
    {
        Draft().PostApproved(Number(1), Now, Checker, BillDate, Window).Error.Code.ShouldBe(AdjustmentsErrors.AdjustmentNotPendingApprovalCode);
        Draft().ReturnToDraft("x").Error.Code.ShouldBe(AdjustmentsErrors.AdjustmentNotPendingApprovalCode);

        var billNumber = DocumentNumber.Create(DocumentType.Bill, FiscalYear.FromDate(Today), "BIL", 1);
        var lastYear = DocumentNumber.Create(DocumentType.Adjustment, FiscalYear.FromDate(Today.AddYears(-1)), "ADJ", 1);
        Should.Throw<ArgumentException>(() => Pending().PostApproved(billNumber, Now, Checker, BillDate, Window));
        Should.Throw<ArgumentException>(() => Pending().PostApproved(lastYear, Now, Checker, BillDate, Window));
    }

    [Fact]
    public void BR605_a_rejection_makes_the_adjustment_a_draft_again()
    {
        var adjustment = Pending();
        adjustment.ClearDomainEvents();

        adjustment.ReturnToDraft("Ask the consultant first").IsSuccess.ShouldBeTrue();

        adjustment.Status.ShouldBe(AdjustmentStatus.Draft);
        adjustment.DomainEvents.ShouldHaveSingleItem().ShouldBe(new AdjustmentReturnedToDraft(adjustment.Id, "Ask the consultant first"));
        adjustment.Update(Today, AdjustmentKind.Discount, Money.Of(50m), "Half of it", BillDate, Window).IsSuccess.ShouldBeTrue("a draft can be corrected");
    }

    [Fact]
    public void BR005_an_adjustment_is_never_deletable()
        => typeof(IHardDeletable).IsAssignableFrom(typeof(Adjustment)).ShouldBeFalse();

    private static DocumentNumber Number(int sequence) => DocumentNumber.Create(DocumentType.Adjustment, FiscalYear.FromDate(Today), "ADJ", sequence);

    private static Adjustment Draft(AdjustmentKind kind = AdjustmentKind.WriteOff, decimal amount = 126m)
        => Adjustment.Create(Bill, Payer, BillDate, Today, kind, Money.Of(amount), "Patient cannot pay", Window).Value;

    private static Adjustment Pending()
    {
        var adjustment = Draft();
        adjustment.SubmitForApproval(BillDate, Window).IsSuccess.ShouldBeTrue();

        return adjustment;
    }
}
