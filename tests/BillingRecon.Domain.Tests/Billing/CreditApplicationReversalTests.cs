using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Billing;

/// <summary>
/// Reversing an application of a credit note's credit, in the aggregate (ADR 0028; BR-212, BR-213, BR-605): a later
/// application waits for a second person, an approval makes its amount available again and marks the row, a
/// rejection leaves it in force, and the application made at posting is never reversed.
/// </summary>
public sealed class CreditApplicationReversalTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly BusinessDateWindow Window = new(Today, maxBackdateDays: 7);
    private static readonly BillId OwnBill = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3610"));
    private static readonly BillId OtherBill = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3611"));
    private static readonly PayerId Payer = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3602"));
    private static readonly PatientId Patient = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3601"));
    private static readonly UserId Cashier = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3620"));
    private static readonly UserId Checker = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3621"));
    private static readonly DateTimeOffset PostedAt = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ReversedAt = new(2026, 11, 20, 15, 30, 0, TimeSpan.Zero);

    [Fact]
    public void BR212_the_application_to_its_own_bill_at_posting_is_marked_and_a_later_one_is_not()
    {
        var (creditNote, atPosting, later) = Applied();

        (atPosting.BillId, atPosting.Amount, atPosting.AppliedAtPosting).ShouldBe((OwnBill, Money.Of(26m), true));
        (later.BillId, later.Amount, later.AppliedAtPosting).ShouldBe((OtherBill, Money.Of(60m), false));
        creditNote.AvailableAmount.ShouldBe(Money.Of(40m));

        // Only once, and only as the first thing after posting.
        Should.Throw<InvalidOperationException>(() => creditNote.ApplyToOwnBillAtPosting(Money.Of(1m), PostedAt, Checker));
        Should.Throw<InvalidOperationException>(() => Draft().ApplyToOwnBillAtPosting(Money.Of(1m), PostedAt, Checker));
    }

    [Fact]
    public void ADR0028_the_application_made_at_posting_is_never_reversed()
    {
        var (creditNote, atPosting, _) = Applied();
        creditNote.ClearDomainEvents();

        var refused = creditNote.RequestApplicationReversal(atPosting.Id, "Wrong bill");

        refused.Error.Code.ShouldBe(BillingErrors.CreditApplicationMadeAtPostingCode);
        refused.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        atPosting.Status.ShouldBe(CreditNoteAllocationStatus.Active);
        creditNote.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void ADR0028_a_later_application_waits_for_a_second_person_and_stays_applied_until_the_approval()
    {
        var (creditNote, _, later) = Applied();
        creditNote.ClearDomainEvents();

        creditNote.RequestApplicationReversal(later.Id, "  Applied to the wrong bill  ").IsSuccess.ShouldBeTrue();

        later.Status.ShouldBe(CreditNoteAllocationStatus.PendingReversal);
        later.IsApplied.ShouldBeTrue("the amount is still applied to the bill");
        creditNote.AvailableAmount.ShouldBe(Money.Of(40m), "nothing moved yet");
        creditNote.DomainEvents.ShouldHaveSingleItem().ShouldBe(
            new CreditNoteApplicationReversalRequested("CN/26-27/000007", later.Id, OtherBill, Money.Of(60m), "Applied to the wrong bill"));

        // One decision at a time; more credit can still be applied meanwhile.
        creditNote.RequestApplicationReversal(later.Id, "Again").Error.Code.ShouldBe(BillingErrors.CreditApplicationPendingReversalCode);
        creditNote.Apply(OtherBill, Money.Of(40m), ReversedAt, Cashier).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ADR0028_the_approval_marks_the_application_reversed_and_makes_its_amount_available_again()
    {
        var (creditNote, atPosting, later) = Applied();
        creditNote.RequestApplicationReversal(later.Id, "Applied to the wrong bill").IsSuccess.ShouldBeTrue();
        creditNote.ClearDomainEvents();

        var reversed = creditNote.ReverseApplication(later.Id, "Applied to the wrong bill", ReversedAt, Checker).Value;

        reversed.ShouldBeSameAs(later);
        later.Status.ShouldBe(CreditNoteAllocationStatus.Reversed);
        later.IsApplied.ShouldBeFalse();
        (later.ReversedAtUtc, later.ReversedByUserId, later.ReversalReason).ShouldBe((ReversedAt, Checker, "Applied to the wrong bill"));
        (later.Amount, later.BillId).ShouldBe((Money.Of(60m), OtherBill), "the row keeps what it was (BR-006)");
        creditNote.AvailableAmount.ShouldBe(Money.Of(100m));
        creditNote.Total.ShouldBe(Money.Of(126m), "a posted credit note keeps its total (BR-006)");
        creditNote.Allocations.Count.ShouldBe(2, "nothing is removed (BR-005)");
        atPosting.Status.ShouldBe(CreditNoteAllocationStatus.Active);
        creditNote.DomainEvents.ShouldHaveSingleItem().ShouldBe(
            new CreditNoteApplicationReversed("CN/26-27/000007", later.Id, OtherBill, Money.Of(60m), Money.Of(100m), "Applied to the wrong bill"));

        // Reversed once and for all; the freed credit can be applied again.
        creditNote.RequestApplicationReversal(later.Id, "Again").Error.Code.ShouldBe(BillingErrors.CreditApplicationReversedCode);
        creditNote.ReverseApplication(later.Id, "Again", ReversedAt, Checker).Error.Code.ShouldBe(BillingErrors.CreditApplicationReversedCode);
        creditNote.Apply(OtherBill, Money.Of(100m), ReversedAt, Cashier).IsSuccess.ShouldBeTrue();
        creditNote.AvailableAmount.ShouldBe(Money.Zero);
    }

    [Fact]
    public void BR605_a_rejected_reversal_leaves_the_application_in_force()
    {
        var (creditNote, _, later) = Applied();
        creditNote.RequestApplicationReversal(later.Id, "Applied to the wrong bill").IsSuccess.ShouldBeTrue();
        creditNote.ClearDomainEvents();

        creditNote.RejectApplicationReversal(later.Id, "The bill is right").IsSuccess.ShouldBeTrue();

        later.Status.ShouldBe(CreditNoteAllocationStatus.Active);
        later.ReversedAtUtc.ShouldBeNull();
        creditNote.AvailableAmount.ShouldBe(Money.Of(40m));
        creditNote.DomainEvents.ShouldHaveSingleItem().ShouldBe(new CreditNoteApplicationReversalRejected("CN/26-27/000007", later.Id, "The bill is right"));
        creditNote.RequestApplicationReversal(later.Id, "Asked again").IsSuccess.ShouldBeTrue("it can be asked for again");
    }

    [Fact]
    public void An_application_nobody_has_or_that_does_not_wait_has_no_reversal_to_decide()
    {
        var (creditNote, _, later) = Applied();
        var unknown = new CreditNoteAllocationId(Guid.NewGuid());

        creditNote.RequestApplicationReversal(unknown, "x").Error.Code.ShouldBe(BillingErrors.CreditApplicationNotFoundCode);
        creditNote.RequestApplicationReversal(unknown, "x").Error.Kind.ShouldBe(ErrorKind.NotFound);
        creditNote.CheckApplicationAwaitsReversal(later.Id).Error.Code.ShouldBe(BillingErrors.CreditApplicationNotPendingReversalCode);
        creditNote.ReverseApplication(later.Id, "x", ReversedAt, Checker).Error.Code.ShouldBe(BillingErrors.CreditApplicationNotPendingReversalCode);
        creditNote.RejectApplicationReversal(later.Id, "x").Error.Code.ShouldBe(BillingErrors.CreditApplicationNotPendingReversalCode);
        later.Status.ShouldBe(CreditNoteAllocationStatus.Active);
        Should.Throw<ArgumentException>(() => creditNote.RequestApplicationReversal(later.Id, " "));

        creditNote.RequestApplicationReversal(later.Id, "x").IsSuccess.ShouldBeTrue();
        Should.Throw<ArgumentException>(() => creditNote.ReverseApplication(later.Id, "x", ReversedAt, default));
        later.Status.ShouldBe(CreditNoteAllocationStatus.PendingReversal);
    }

    [Fact]
    public void The_posting_of_a_reversal_is_recorded_once_and_only_on_a_reversed_application_of_this_credit_note()
    {
        var (creditNote, atPosting, later) = Applied();
        var posting = new LedgerPostingId(Guid.NewGuid());

        Should.Throw<InvalidOperationException>(() => creditNote.AttachApplicationReversalPosting(later, posting));

        creditNote.RequestApplicationReversal(later.Id, "x").IsSuccess.ShouldBeTrue();
        creditNote.ReverseApplication(later.Id, "x", ReversedAt, Checker).IsSuccess.ShouldBeTrue();

        Should.Throw<ArgumentException>(() => creditNote.AttachApplicationReversalPosting(later, default));
        Should.Throw<ArgumentException>(() => Applied().CreditNote.AttachApplicationReversalPosting(later, posting));
        creditNote.AttachApplicationReversalPosting(later, posting);
        later.ReversalPostingId.ShouldBe(posting);
        Should.Throw<InvalidOperationException>(() => creditNote.AttachApplicationReversalPosting(later, new LedgerPostingId(Guid.NewGuid())));
        Should.Throw<InvalidOperationException>(() => creditNote.AttachApplicationReversalPosting(atPosting, posting));
    }

    // 2 x 60.00 at 5%: 126.00.
    private static CreditNote Draft() => CreditNote.Create(
        OwnBill,
        Payer,
        Patient,
        Today,
        Today,
        "Dressing not done",
        [new CreditNoteLineDetails { Description = "Dressing", Quantity = Quantity.Of(2m), UnitPrice = UnitPrice.Of(60m), TaxRate = Percent.Of(5m) }],
        Window).Value;

    // Posted, 26.00 applied to its own bill at posting, then 60.00 to another bill: 40.00 left. The applications get
    // the ids the persistence layer would give them, because a reversal names an application by its id.
    private static (CreditNote CreditNote, CreditNoteAllocation AtPosting, CreditNoteAllocation Later) Applied()
    {
        var creditNote = Draft();
        creditNote.SubmitForApproval(Today, Window).IsSuccess.ShouldBeTrue();
        creditNote.PostApproved(DocumentNumber.Create(DocumentType.CreditNote, FiscalYear.FromDate(Today), "CN", 7), PostedAt, Checker, Today, Window).IsSuccess.ShouldBeTrue();
        var atPosting = WithId(creditNote.ApplyToOwnBillAtPosting(Money.Of(26m), PostedAt, Checker).Value);
        var later = WithId(creditNote.Apply(OtherBill, Money.Of(60m), PostedAt.AddHours(1), Cashier).Value);

        return (creditNote, atPosting, later);
    }

    private static CreditNoteAllocation WithId(CreditNoteAllocation allocation)
    {
        typeof(Entity<CreditNoteAllocationId>).GetProperty(nameof(Entity<CreditNoteAllocationId>.Id))!.SetValue(allocation, new CreditNoteAllocationId(Guid.NewGuid()));

        return allocation;
    }
}
