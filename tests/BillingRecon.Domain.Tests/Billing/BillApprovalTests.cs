using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Billing;

/// <summary>
/// A bill that needs a second person (BR-207, BR-605, BR-606; docs/domain-model.md, section 5.1): submitted, it waits
/// and is locked; approved, it is posted; rejected, it is a draft again.
/// </summary>
public sealed class BillApprovalTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly BusinessDateWindow Window = new(Today, maxBackdateDays: 7);
    private static readonly PatientId Patient = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301"));
    private static readonly PayerId Payer = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3302"));
    private static readonly UserId Checker = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3304"));
    private static readonly DateTimeOffset At = new(2026, 11, 20, 9, 15, 0, TimeSpan.Zero);

    [Fact]
    public void BR207_a_submitted_bill_waits_without_a_number_and_raises_BillSubmittedForApproval()
    {
        var bill = Draft(Line());
        bill.ClearDomainEvents();

        var result = bill.SubmitForApproval(Window);

        result.IsSuccess.ShouldBeTrue();
        bill.Status.ShouldBe(BillStatus.PendingApproval);
        bill.Number.ShouldBeNull();
        bill.SequenceNumber.ShouldBeNull();
        bill.PostedAtUtc.ShouldBeNull();
        bill.BalanceDue.ShouldBe(bill.Total);
        bill.DomainEvents.ShouldHaveSingleItem().ShouldBe(new BillSubmittedForApproval(bill.Id, Payer, Money.Of(590m)));
    }

    [Fact]
    public void Only_a_bill_that_could_be_posted_is_submitted()
    {
        var empty = Draft();
        var grownOld = Draft(Line());
        var voided = Draft(Line());
        voided.Void("Duplicate", At, Checker);

        empty.SubmitForApproval(Window).Error.Code.ShouldBe(BillingErrors.BillHasNoLinesCode);
        grownOld.SubmitForApproval(new BusinessDateWindow(Today.AddDays(8), 7)).Error.Code.ShouldBe(BillingErrors.BillDateTooOldCode);
        voided.SubmitForApproval(Window).Error.Code.ShouldBe(BillingErrors.BillNotDraftCode);
        empty.Status.ShouldBe(BillStatus.Draft);
        grownOld.Status.ShouldBe(BillStatus.Draft);
    }

    [Fact]
    public void BR207_a_bill_that_waits_refuses_edit_void_a_second_submission_and_a_direct_posting()
    {
        var bill = Pending();

        Result[] refusals =
        [
            bill.UpdateHeader(new BillHeader { BillDate = Today, DueDate = Today, Notes = "Changed" }, Window),
            bill.ReplaceLines([]),
            bill.Void("Changed my mind", At, Checker),
            bill.SubmitForApproval(Window),
            bill.Post(Number(1), At, Checker, Window),
            bill.CheckIsDraft(),
        ];

        foreach (var refused in refusals)
        {
            refused.Error.Code.ShouldBe(BillingErrors.BillPendingApprovalCode);
            refused.Error.Kind.ShouldBe(ErrorKind.Conflict);
        }

        bill.Status.ShouldBe(BillStatus.PendingApproval);
        bill.Notes.ShouldBeNull();
        bill.Lines.ShouldHaveSingleItem();
        bill.Number.ShouldBeNull();
        bill.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void BR606_the_approval_posts_the_bill_under_its_number_with_the_checker_as_the_poster()
    {
        var bill = Pending();

        var result = bill.PostApproved(Number(7), At, Checker, Window);

        result.IsSuccess.ShouldBeTrue();
        bill.Status.ShouldBe(BillStatus.Posted);
        bill.Number.ShouldBe("BIL/26-27/000007");
        bill.SequenceNumber.ShouldBe(7);
        bill.PostedAtUtc.ShouldBe(At);
        bill.PostedByUserId.ShouldBe(Checker);
        bill.BalanceDue.ShouldBe(bill.Total);
        bill.DomainEvents.ShouldHaveSingleItem().ShouldBe(new BillPosted(bill.Id, "BIL/26-27/000007", Payer, Money.Of(590m), Today));
    }

    [Fact]
    public void BR205_a_bill_that_waited_too_long_cannot_be_approved_and_stays_waiting()
    {
        var bill = Pending();
        var eightDaysLater = new BusinessDateWindow(Today.AddDays(8), 7);

        bill.CheckCanApprove(eightDaysLater).Error.Code.ShouldBe(BillingErrors.BillDateTooOldCode);
        bill.PostApproved(Number(1), At, Checker, eightDaysLater).Error.Code.ShouldBe(BillingErrors.BillDateTooOldCode);

        bill.Status.ShouldBe(BillStatus.PendingApproval);
        bill.Number.ShouldBeNull();

        // The checker can still reject it, and the maker correct its date.
        bill.ReturnToDraft("The bill date is too old now").IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void BR605_a_rejected_bill_is_a_draft_again_and_can_be_corrected_and_submitted_once_more()
    {
        var bill = Pending();

        var result = bill.ReturnToDraft(" Wrong consultant fee ");

        result.IsSuccess.ShouldBeTrue();
        bill.Status.ShouldBe(BillStatus.Draft);
        bill.Number.ShouldBeNull();
        var raised = bill.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<BillReturnedToDraft>();
        raised.ShouldBe(new BillReturnedToDraft(bill.Id, "Wrong consultant fee"));
        ((IHasReason)raised).Reason.ShouldBe("Wrong consultant fee");

        bill.ReplaceLines([Line() with { UnitPrice = UnitPrice.Of(400m) }]).IsSuccess.ShouldBeTrue();
        bill.SubmitForApproval(Window).IsSuccess.ShouldBeTrue();
        bill.Status.ShouldBe(BillStatus.PendingApproval);
    }

    [Fact]
    public void Only_a_bill_that_waits_is_approved_or_rejected()
    {
        var draft = Draft(Line());
        var voided = Draft(Line());
        voided.Void("Duplicate", At, Checker);
        var posted = Draft(Line());
        posted.Post(Number(1), At, Checker, Window);

        draft.CheckCanApprove(Window).Error.Code.ShouldBe(BillingErrors.BillNotPendingApprovalCode);
        draft.PostApproved(Number(2), At, Checker, Window).Error.Code.ShouldBe(BillingErrors.BillNotPendingApprovalCode);
        draft.ReturnToDraft("No").Error.Code.ShouldBe(BillingErrors.BillNotPendingApprovalCode);
        draft.ReturnToDraft("No").Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        voided.CheckCanApprove(Window).Error.Code.ShouldBe(BillingErrors.BillNotDraftCode);
        voided.ReturnToDraft("No").Error.Code.ShouldBe(BillingErrors.BillNotDraftCode);
        posted.CheckCanApprove(Window).Error.Code.ShouldBe(BillingErrors.BillAlreadyPostedCode);
        posted.ReturnToDraft("No").Error.Code.ShouldBe(BillingErrors.BillAlreadyPostedCode);
        draft.Status.ShouldBe(BillStatus.Draft);
        posted.Status.ShouldBe(BillStatus.Posted);
    }

    [Fact]
    public void A_rejection_without_a_reason_is_a_bug_and_throws_and_the_bill_keeps_waiting()
    {
        var bill = Pending();

        Should.Throw<ArgumentException>(() => bill.ReturnToDraft("  "));

        bill.Status.ShouldBe(BillStatus.PendingApproval);
    }

    private static DocumentNumber Number(int sequence) => DocumentNumber.Create(DocumentType.Bill, FiscalYear.Of(2026), "BIL", sequence);

    private static Bill Pending()
    {
        var bill = Draft(Line());
        bill.SubmitForApproval(Window).IsSuccess.ShouldBeTrue();
        bill.ClearDomainEvents();

        return bill;
    }

    private static Bill Draft(params BillLineDetails[] lines)
        => Bill.Create(Patient, Payer, new BillHeader { BillDate = Today, DueDate = Today.AddDays(15) }, lines, Window).Value;

    // 1 x 500.00 at 18%: 500.00 + 90.00.
    private static BillLineDetails Line() => new()
    {
        Description = "OPD consultation",
        Quantity = Quantity.Of(1m),
        UnitPrice = UnitPrice.Of(500m),
        TaxRate = Percent.Of(18m),
    };
}
