using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Domain.Tests.Payments;

/// <summary>
/// Allocating a payment (BR-303, BR-304, BR-405; docs/domain-model.md, section 4.6): each allocation lowers the
/// unapplied amount, never below zero, and is kept with the payment.
/// </summary>
public sealed class PaymentAllocationTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly BusinessDateWindow Window = new(Today, maxBackdateDays: 7);
    private static readonly PayerId Payer = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3302"));
    private static readonly BillId FirstBill = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3311"));
    private static readonly BillId SecondBill = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3312"));
    private static readonly UserId Cashier = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3304"));
    private static readonly DateTimeOffset At = new(2026, 11, 20, 9, 15, 0, TimeSpan.Zero);

    [Fact]
    public void BR303_an_allocation_lowers_the_unapplied_amount_and_is_kept_with_the_payment()
    {
        var payment = Recorded(3000m);
        payment.ClearDomainEvents();

        var allocation = payment.Allocate(FirstBill, Money.Of(1200.50m), At, Cashier).Value;

        payment.UnappliedAmount.ShouldBe(Money.Of(1799.50m));
        payment.Amount.ShouldBe(Money.Of(3000m), "the amount of a payment never changes (BR-006)");
        payment.Allocations.ShouldHaveSingleItem().ShouldBeSameAs(allocation);
        allocation.BillId.ShouldBe(FirstBill);
        allocation.Amount.ShouldBe(Money.Of(1200.50m));
        allocation.Status.ShouldBe(PaymentAllocationStatus.Active);
        allocation.AllocatedAtUtc.ShouldBe(At);
        allocation.AllocatedByUserId.ShouldBe(Cashier);
        allocation.PostingId.ShouldBe(default);
        payment.DomainEvents.ShouldHaveSingleItem().ShouldBe(new PaymentAllocated("PAY/26-27/000017", FirstBill, Money.Of(1200.50m), Money.Of(1799.50m)));
    }

    [Fact]
    public void BR304_a_payment_is_allocated_to_several_bills_until_nothing_is_left()
    {
        var payment = Recorded(3000m);
        payment.ClearDomainEvents();

        payment.Allocate(FirstBill, Money.Of(1000m), At, Cashier).IsSuccess.ShouldBeTrue();
        payment.Allocate(SecondBill, Money.Of(1999.99m), At, Cashier).IsSuccess.ShouldBeTrue();
        payment.Allocate(FirstBill, Money.Of(0.01m), At, allocatedBy: null).IsSuccess.ShouldBeTrue();

        payment.UnappliedAmount.ShouldBe(Money.Zero);
        payment.Allocations.Select(allocation => allocation.Amount).ShouldBe([Money.Of(1000m), Money.Of(1999.99m), Money.Of(0.01m)]);
        payment.Allocations[2].AllocatedByUserId.ShouldBeNull();
        payment.DomainEvents.Count.ShouldBe(3);
        payment.Status.ShouldBe(PaymentStatus.Posted, "a payment that is fully applied is still a posted payment");
    }

    [Fact]
    public void BR303_more_than_the_unapplied_amount_is_refused_and_the_payment_stays_as_it_was()
    {
        var payment = Recorded(3000m);
        payment.Allocate(FirstBill, Money.Of(2500m), At, Cashier);
        payment.ClearDomainEvents();

        var refused = payment.Allocate(SecondBill, Money.Of(500.01m), At, Cashier);

        refused.Error.Code.ShouldBe(PaymentsErrors.AllocationExceedsUnappliedCode);
        refused.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        refused.Error.Message.ShouldContain("PAY/26-27/000017");
        refused.Error.Message.ShouldContain("500.00");
        payment.UnappliedAmount.ShouldBe(Money.Of(500m));
        payment.Allocations.ShouldHaveSingleItem();
        payment.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void BR303_the_check_for_a_whole_request_answers_without_changing_anything()
    {
        var payment = Recorded(3000m);

        payment.CheckCanAllocate(Money.Of(3000m)).IsSuccess.ShouldBeTrue();
        payment.CheckCanAllocate(Money.Of(3000.01m)).Error.Code.ShouldBe(PaymentsErrors.AllocationExceedsUnappliedCode);
        payment.UnappliedAmount.ShouldBe(Money.Of(3000m));
        payment.Allocations.ShouldBeEmpty();
    }

    [Fact]
    public void An_empty_bill_id_and_an_amount_that_is_not_above_zero_are_bugs_and_throw()
    {
        var payment = Recorded(3000m);

        Should.Throw<ArgumentException>(() => payment.Allocate(default, Money.Of(1m), At, Cashier));
        Should.Throw<ArgumentException>(() => payment.Allocate(FirstBill, Money.Zero, At, Cashier)).Message.ShouldContain("BR-303");
        Should.Throw<ArgumentException>(() => payment.Allocate(FirstBill, Money.Of(-1m), At, Cashier));
        payment.UnappliedAmount.ShouldBe(Money.Of(3000m));
        payment.Allocations.ShouldBeEmpty();
    }

    [Fact]
    public void BR006_an_allocation_keeps_the_one_ledger_posting_it_was_made_with()
    {
        var payment = Recorded(3000m);
        var allocation = payment.Allocate(FirstBill, Money.Of(1000m), At, Cashier).Value;
        var posting = new LedgerPostingId(Guid.NewGuid());

        payment.AttachAllocationPosting(allocation, posting);

        allocation.PostingId.ShouldBe(posting);
        Should.Throw<InvalidOperationException>(() => payment.AttachAllocationPosting(allocation, new LedgerPostingId(Guid.NewGuid())));
        allocation.PostingId.ShouldBe(posting);

        var second = payment.Allocate(SecondBill, Money.Of(1000m), At, Cashier).Value;
        Should.Throw<ArgumentException>(() => payment.AttachAllocationPosting(second, default));

        // An allocation of another payment is not this payment's to change.
        var other = Recorded(500m);
        var theirs = other.Allocate(FirstBill, Money.Of(100m), At, Cashier).Value;
        Should.Throw<ArgumentException>(() => payment.AttachAllocationPosting(theirs, posting));
    }

    [Fact]
    public void BR006_nothing_public_changes_an_allocation_and_it_cannot_be_deleted()
    {
        typeof(PaymentAllocation).GetProperties().ShouldAllBe(property => property.SetMethod == null || !property.SetMethod.IsPublic);
        typeof(PaymentAllocation).GetConstructors().ShouldBeEmpty("an allocation is made only by its payment");
        typeof(IHardDeletable).IsAssignableFrom(typeof(PaymentAllocation)).ShouldBeFalse();
    }

    private static Payment Recorded(decimal amount) => Payment.Record(
        Payer,
        Today,
        PaymentMethod.UPI,
        "412345678901",
        Money.Of(amount),
        notes: null,
        DocumentNumber.Create(DocumentType.Payment, FiscalYear.Of(2026), "PAY", 17),
        Window).Value;
}
