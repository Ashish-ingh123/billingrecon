using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Domain.Tests.Payments;

/// <summary>
/// A payment pays a refund out of what it has unapplied (BR-306, BR-307; ADR 0029): only while it is posted, never more
/// than is unapplied, and its allocations are untouched.
/// </summary>
public sealed class PaymentRefundTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly BusinessDateWindow Window = new(Today, maxBackdateDays: 7);
    private static readonly PayerId Payer = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3802"));
    private static readonly BillId Bill = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3810"));
    private static readonly UserId Cashier = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3820"));
    private static readonly DateTimeOffset At = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BR307_a_refund_takes_what_is_unapplied_and_the_allocations_stay()
    {
        var payment = Recorded();
        payment.Allocate(Bill, Money.Of(600m), At, Cashier).IsSuccess.ShouldBeTrue();
        payment.ClearDomainEvents();

        payment.CheckCanRefund().IsSuccess.ShouldBeTrue();
        payment.PayRefund("REF/26-27/000001", Money.Of(150m)).IsSuccess.ShouldBeTrue();

        payment.UnappliedAmount.ShouldBe(Money.Of(250m));
        payment.Amount.ShouldBe(Money.Of(1000m), "a payment keeps its amount (BR-006)");
        payment.Allocations.ShouldHaveSingleItem().Status.ShouldBe(PaymentAllocationStatus.Active);
        payment.DomainEvents.ShouldHaveSingleItem().ShouldBe(new PaymentRefunded("PAY/26-27/000007", "REF/26-27/000001", Money.Of(150m), Money.Of(250m)));

        payment.PayRefund("REF/26-27/000002", Money.Of(250m)).IsSuccess.ShouldBeTrue("all that is left");
        payment.UnappliedAmount.ShouldBe(Money.Zero);
    }

    [Fact]
    public void BR307_more_than_is_unapplied_is_refused_and_nothing_changes()
    {
        var payment = Recorded();
        payment.ClearDomainEvents();

        var refused = payment.PayRefund("REF/26-27/000001", Money.Of(1000.01m));

        refused.Error.Code.ShouldBe(PaymentsErrors.RefundExceedsAvailableCode);
        payment.UnappliedAmount.ShouldBe(Money.Of(1000m));
        payment.DomainEvents.ShouldBeEmpty();
        Should.Throw<ArgumentException>(() => payment.PayRefund("REF/26-27/000001", Money.Zero));
        Should.Throw<ArgumentException>(() => payment.PayRefund(" ", Money.Of(1m)));
    }

    [Fact]
    public void BR306_a_payment_that_waits_for_its_reversal_or_was_reversed_refunds_nothing()
    {
        var waiting = Recorded();
        waiting.RequestReversal("Cheque dishonoured").IsSuccess.ShouldBeTrue();

        waiting.CheckCanRefund().Error.Code.ShouldBe(PaymentsErrors.PaymentPendingReversalCode);
        waiting.PayRefund("REF/26-27/000001", Money.Of(1m)).Error.Code.ShouldBe(PaymentsErrors.PaymentPendingReversalCode);

        waiting.Reverse("Cheque dishonoured", At, Cashier).IsSuccess.ShouldBeTrue();
        waiting.CheckCanRefund().Error.Code.ShouldBe(PaymentsErrors.PaymentReversedCode);
        waiting.PayRefund("REF/26-27/000001", Money.Of(1m)).Error.Code.ShouldBe(PaymentsErrors.PaymentReversedCode);
    }

    [Fact]
    public void BR306_a_payment_that_paid_a_refund_is_never_reversed_by_the_aggregate_either()
    {
        var payment = Recorded();
        payment.PayRefund("REF/26-27/000001", Money.Of(100m)).IsSuccess.ShouldBeTrue();
        payment.RequestReversal("Cheque dishonoured").IsSuccess.ShouldBeTrue("the use case refuses this first (payment_refunded)");

        Should.Throw<InvalidOperationException>(() => payment.Reverse("Cheque dishonoured", At, Cashier)).Message.ShouldContain("refunded");
    }

    private static Payment Recorded()
    {
        var number = DocumentNumber.Create(DocumentType.Payment, FiscalYear.FromDate(Today), "PAY", 7);

        return Payment.Record(Payer, Today, PaymentMethod.Cheque, "000123", Money.Of(1000m), notes: null, number, Window).Value;
    }
}
