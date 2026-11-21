using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Domain.Tests.Payments;

/// <summary>
/// Reversing an allocation and reversing a payment, in the aggregate (BR-305, BR-306, BR-605; docs/domain-model.md,
/// section 5.3): each waits for a second person, an approval takes the money back and marks the rows, a rejection
/// leaves them as they were, and nothing is ever removed.
/// </summary>
public sealed class PaymentReversalTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly BusinessDateWindow Window = new(Today, maxBackdateDays: 7);
    private static readonly PayerId Payer = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3502"));
    private static readonly BillId FirstBill = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3510"));
    private static readonly BillId SecondBill = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3511"));
    private static readonly UserId Cashier = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3520"));
    private static readonly UserId Checker = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3521"));
    private static readonly DateTimeOffset AllocatedAt = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ReversedAt = new(2026, 11, 20, 15, 30, 0, TimeSpan.Zero);

    [Fact]
    public void BR305_an_allocation_waits_for_a_second_person_and_stays_applied_until_the_approval()
    {
        var (payment, first, _) = Allocated();
        payment.ClearDomainEvents();

        payment.RequestAllocationReversal(first.Id, "  Wrong bill  ").IsSuccess.ShouldBeTrue();

        first.Status.ShouldBe(PaymentAllocationStatus.PendingReversal);
        first.IsApplied.ShouldBeTrue("the amount is still applied to the bill");
        payment.UnappliedAmount.ShouldBe(Money.Of(400m), "nothing moved yet");
        payment.Status.ShouldBe(PaymentStatus.Posted);
        payment.DomainEvents.ShouldHaveSingleItem().ShouldBe(new PaymentAllocationReversalRequested("PAY/26-27/000007", first.Id, FirstBill, Money.Of(500m), "Wrong bill"));

        // One decision at a time, for the allocation and for its payment.
        payment.RequestAllocationReversal(first.Id, "Again").Error.Code.ShouldBe(PaymentsErrors.AllocationPendingReversalCode);
        payment.RequestReversal("Cheque dishonoured").Error.Code.ShouldBe(PaymentsErrors.AllocationPendingReversalCode);
        payment.Status.ShouldBe(PaymentStatus.Posted);
    }

    [Fact]
    public void BR305_the_approval_marks_the_allocation_reversed_and_gives_its_amount_back_to_the_payment()
    {
        var (payment, first, second) = Allocated();
        payment.RequestAllocationReversal(first.Id, "Wrong bill").IsSuccess.ShouldBeTrue();
        payment.ClearDomainEvents();

        var reversed = payment.ReverseAllocation(first.Id, "Wrong bill", ReversedAt, Checker).Value;

        reversed.ShouldBeSameAs(first);
        first.Status.ShouldBe(PaymentAllocationStatus.Reversed);
        first.IsApplied.ShouldBeFalse();
        (first.ReversedAtUtc, first.ReversedByUserId, first.ReversalReason).ShouldBe((ReversedAt, Checker, "Wrong bill"));
        (first.Amount, first.BillId).ShouldBe((Money.Of(500m), FirstBill), "the row keeps what it was (BR-006)");
        payment.UnappliedAmount.ShouldBe(Money.Of(900m));
        payment.Allocations.Count.ShouldBe(2, "nothing is removed (BR-005)");
        second.Status.ShouldBe(PaymentAllocationStatus.Active);
        payment.DomainEvents.ShouldHaveSingleItem().ShouldBe(new PaymentAllocationReversed("PAY/26-27/000007", first.Id, FirstBill, Money.Of(500m), Money.Of(900m), "Wrong bill"));

        // Reversed once and for all; the amount can be allocated again.
        payment.RequestAllocationReversal(first.Id, "Again").Error.Code.ShouldBe(PaymentsErrors.AllocationReversedCode);
        payment.ReverseAllocation(first.Id, "Again", ReversedAt, Checker).Error.Code.ShouldBe(PaymentsErrors.AllocationReversedCode);
        payment.Allocate(SecondBill, Money.Of(900m), ReversedAt, Cashier).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void BR605_a_rejected_reversal_leaves_the_allocation_in_force()
    {
        var (payment, first, _) = Allocated();
        payment.RequestAllocationReversal(first.Id, "Wrong bill").IsSuccess.ShouldBeTrue();
        payment.ClearDomainEvents();

        payment.RejectAllocationReversal(first.Id, "The bill is right").IsSuccess.ShouldBeTrue();

        first.Status.ShouldBe(PaymentAllocationStatus.Active);
        first.ReversedAtUtc.ShouldBeNull();
        payment.UnappliedAmount.ShouldBe(Money.Of(400m));
        payment.DomainEvents.ShouldHaveSingleItem().ShouldBe(new PaymentAllocationReversalRejected("PAY/26-27/000007", first.Id, "The bill is right"));
        payment.RequestAllocationReversal(first.Id, "Asked again").IsSuccess.ShouldBeTrue("it can be asked for again");
    }

    [Fact]
    public void An_allocation_nobody_has_or_that_does_not_wait_has_no_reversal_to_decide()
    {
        var (payment, first, _) = Allocated();
        var unknown = new PaymentAllocationId(Guid.NewGuid());

        payment.RequestAllocationReversal(unknown, "x").Error.Code.ShouldBe(PaymentsErrors.AllocationNotFoundCode);
        payment.RequestAllocationReversal(unknown, "x").Error.Kind.ShouldBe(ErrorKind.NotFound);
        payment.CheckAllocationAwaitsReversal(first.Id).Error.Code.ShouldBe(PaymentsErrors.AllocationNotPendingReversalCode);
        payment.ReverseAllocation(first.Id, "x", ReversedAt, Checker).Error.Code.ShouldBe(PaymentsErrors.AllocationNotPendingReversalCode);
        payment.RejectAllocationReversal(first.Id, "x").Error.Code.ShouldBe(PaymentsErrors.AllocationNotPendingReversalCode);
        first.Status.ShouldBe(PaymentAllocationStatus.Active);
        Should.Throw<ArgumentException>(() => payment.RequestAllocationReversal(first.Id, " "));
    }

    [Fact]
    public void BR306_a_payment_that_waits_for_its_reversal_can_neither_be_allocated_nor_asked_again()
    {
        var (payment, first, _) = Allocated();
        payment.ClearDomainEvents();

        payment.RequestReversal("Cheque dishonoured").IsSuccess.ShouldBeTrue();

        payment.Status.ShouldBe(PaymentStatus.PendingReversal);
        payment.UnappliedAmount.ShouldBe(Money.Of(400m), "nothing moved yet");
        payment.DomainEvents.ShouldHaveSingleItem().ShouldBe(new PaymentReversalRequested("PAY/26-27/000007", Money.Of(1000m), "Cheque dishonoured"));

        payment.CheckCanAllocate(Money.Of(1m)).Error.Code.ShouldBe(PaymentsErrors.PaymentPendingReversalCode);
        payment.Allocate(SecondBill, Money.Of(1m), ReversedAt, Cashier).Error.Code.ShouldBe(PaymentsErrors.PaymentPendingReversalCode);
        payment.RequestReversal("Again").Error.Code.ShouldBe(PaymentsErrors.PaymentPendingReversalCode);
        payment.RequestAllocationReversal(first.Id, "x").Error.Code.ShouldBe(PaymentsErrors.PaymentPendingReversalCode);
        payment.RequestReversal("Again").Error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public void BR306_the_approval_reverses_every_allocation_still_applied_and_then_the_payment_which_holds_nothing_afterwards()
    {
        var (payment, first, second) = Allocated();
        payment.RequestReversal("Cheque dishonoured").IsSuccess.ShouldBeTrue();
        payment.ClearDomainEvents();

        var takenBack = payment.Reverse("Cheque dishonoured", ReversedAt, Checker).Value;

        // Both allocations, in the order of their bills, each reversed with the reason of the payment.
        takenBack.ShouldBe([first, second]);
        takenBack.ShouldAllBe(allocation => allocation.Status == PaymentAllocationStatus.Reversed && allocation.ReversalReason == "Cheque dishonoured" && allocation.ReversedByUserId == Checker);
        payment.Status.ShouldBe(PaymentStatus.Reversed);
        payment.UnappliedAmount.ShouldBe(Money.Zero, "a reversed payment holds nothing (BR-306)");
        payment.Amount.ShouldBe(Money.Of(1000m), "the amount is what was received (BR-006)");
        (payment.ReversedAtUtc, payment.ReversedByUserId, payment.ReversalReason).ShouldBe((ReversedAt, Checker, "Cheque dishonoured"));
        payment.DomainEvents.ShouldBe(
        [
            new PaymentAllocationReversed("PAY/26-27/000007", first.Id, FirstBill, Money.Of(500m), Money.Of(900m), "Cheque dishonoured"),
            new PaymentAllocationReversed("PAY/26-27/000007", second.Id, SecondBill, Money.Of(100m), Money.Of(1000m), "Cheque dishonoured"),
            new PaymentReversed("PAY/26-27/000007", Payer, Money.Of(1000m), "Cheque dishonoured"),
        ]);

        // Reversed for good.
        payment.Allocate(SecondBill, Money.Of(1m), ReversedAt, Cashier).Error.Code.ShouldBe(PaymentsErrors.PaymentReversedCode);
        payment.RequestReversal("Again").Error.Code.ShouldBe(PaymentsErrors.PaymentReversedCode);
        payment.Reverse("Again", ReversedAt, Checker).Error.Code.ShouldBe(PaymentsErrors.PaymentReversedCode);
        payment.RejectReversal("No").Error.Code.ShouldBe(PaymentsErrors.PaymentReversedCode);
    }

    [Fact]
    public void BR306_an_allocation_reversed_earlier_is_left_alone_and_a_payment_without_allocations_is_reversed_all_the_same()
    {
        var (payment, first, second) = Allocated();
        payment.RequestAllocationReversal(first.Id, "Wrong bill").IsSuccess.ShouldBeTrue();
        payment.ReverseAllocation(first.Id, "Wrong bill", AllocatedAt, Checker).IsSuccess.ShouldBeTrue();
        payment.RequestReversal("Cheque dishonoured").IsSuccess.ShouldBeTrue();

        payment.Reverse("Cheque dishonoured", ReversedAt, Checker).Value.ShouldBe([second]);
        (first.ReversalReason, first.ReversedAtUtc).ShouldBe(("Wrong bill", AllocatedAt), "its own reversal stands");

        var untouched = Recorded();
        untouched.RequestReversal("Recorded twice").IsSuccess.ShouldBeTrue();
        untouched.Reverse("Recorded twice", ReversedAt, Checker).Value.ShouldBeEmpty();
        (untouched.Status, untouched.UnappliedAmount).ShouldBe((PaymentStatus.Reversed, Money.Zero));
    }

    [Fact]
    public void BR605_a_rejected_reversal_leaves_the_payment_posted_and_usable()
    {
        var (payment, _, _) = Allocated();
        payment.RequestReversal("Cheque dishonoured").IsSuccess.ShouldBeTrue();
        payment.ClearDomainEvents();

        payment.RejectReversal("The bank cleared it").IsSuccess.ShouldBeTrue();

        payment.Status.ShouldBe(PaymentStatus.Posted);
        payment.ReversedAtUtc.ShouldBeNull();
        payment.DomainEvents.ShouldHaveSingleItem().ShouldBe(new PaymentReversalRejected("PAY/26-27/000007", "The bank cleared it"));
        payment.Allocate(SecondBill, Money.Of(400m), ReversedAt, Cashier).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_payment_nobody_asked_to_reverse_has_no_reversal_to_decide_and_a_checker_is_a_user()
    {
        var payment = Recorded();

        payment.CheckAwaitsReversal().Error.Code.ShouldBe(PaymentsErrors.PaymentNotPendingReversalCode);
        payment.Reverse("x", ReversedAt, Checker).Error.Code.ShouldBe(PaymentsErrors.PaymentNotPendingReversalCode);
        payment.RejectReversal("x").Error.Code.ShouldBe(PaymentsErrors.PaymentNotPendingReversalCode);

        payment.RequestReversal("x").IsSuccess.ShouldBeTrue();
        Should.Throw<ArgumentException>(() => payment.Reverse("x", ReversedAt, default));
        Should.Throw<ArgumentException>(() => payment.Reverse(" ", ReversedAt, Checker));
        payment.Status.ShouldBe(PaymentStatus.PendingReversal);
    }

    [Fact]
    public void The_postings_of_a_reversal_are_recorded_once_and_only_on_what_was_reversed()
    {
        var (payment, first, second) = Allocated();
        var posting = new LedgerPostingId(Guid.NewGuid());

        Should.Throw<InvalidOperationException>(() => payment.AttachAllocationReversalPosting(first, posting));
        Should.Throw<InvalidOperationException>(() => payment.AttachReversalPosting(posting));

        payment.RequestReversal("Cheque dishonoured").IsSuccess.ShouldBeTrue();
        payment.Reverse("Cheque dishonoured", ReversedAt, Checker).IsSuccess.ShouldBeTrue();

        Should.Throw<ArgumentException>(() => payment.AttachAllocationReversalPosting(first, default));
        payment.AttachAllocationReversalPosting(first, posting);
        first.ReversalPostingId.ShouldBe(posting);
        Should.Throw<InvalidOperationException>(() => payment.AttachAllocationReversalPosting(first, new LedgerPostingId(Guid.NewGuid())));
        Should.Throw<ArgumentException>(() => Recorded().AttachAllocationReversalPosting(second, posting));

        Should.Throw<ArgumentException>(() => payment.AttachReversalPosting(default));
        payment.AttachReversalPosting(posting);
        payment.ReversalPostingId.ShouldBe(posting);
        Should.Throw<InvalidOperationException>(() => payment.AttachReversalPosting(new LedgerPostingId(Guid.NewGuid())));
    }

    // A payment of 1,000.00.
    private static Payment Recorded()
    {
        var number = DocumentNumber.Create(DocumentType.Payment, FiscalYear.FromDate(Today), "PAY", 7);

        return Payment.Record(Payer, Today, PaymentMethod.Cheque, "000123", Money.Of(1000m), notes: null, number, Window).Value;
    }

    // 500.00 to the first bill and 100.00 to the second: 400.00 unapplied. The allocations get the ids the
    // persistence layer would give them, because a reversal names an allocation by its id.
    private static (Payment Payment, PaymentAllocation First, PaymentAllocation Second) Allocated()
    {
        var payment = Recorded();
        var first = WithId(payment.Allocate(FirstBill, Money.Of(500m), AllocatedAt, Cashier).Value);
        var second = WithId(payment.Allocate(SecondBill, Money.Of(100m), AllocatedAt, Cashier).Value);

        return (payment, first, second);
    }

    private static PaymentAllocation WithId(PaymentAllocation allocation)
    {
        typeof(Entity<PaymentAllocationId>).GetProperty(nameof(Entity<PaymentAllocationId>.Id))!.SetValue(allocation, new PaymentAllocationId(Guid.NewGuid()));

        return allocation;
    }
}
