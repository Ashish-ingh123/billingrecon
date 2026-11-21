using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Billing;

/// <summary>
/// Applying an amount to a posted bill (BR-208, BR-303, BR-006): the balance due falls, the status follows the
/// balance, and nothing else about the bill changes.
/// </summary>
public sealed class BillSettlementTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly BusinessDateWindow Window = new(Today, maxBackdateDays: 7);
    private static readonly PatientId Patient = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301"));
    private static readonly PayerId Payer = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3302"));
    private static readonly DateTimeOffset PostedAt = new(2026, 11, 20, 9, 15, 0, TimeSpan.Zero);

    [Fact]
    public void BR208_a_part_payment_leaves_the_bill_partially_paid_and_the_rest_settles_it()
    {
        var bill = Posted();
        bill.ClearDomainEvents();

        bill.ApplyCredit(Money.Of(200m)).IsSuccess.ShouldBeTrue();

        bill.BalanceDue.ShouldBe(Money.Of(390m));
        bill.Status.ShouldBe(BillStatus.PartiallyPaid);
        bill.DomainEvents.ShouldHaveSingleItem().ShouldBe(new BillCreditApplied(bill.Id, "BIL/26-27/000042", Money.Of(200m), Money.Of(390m)));

        bill.ApplyCredit(Money.Of(389.99m)).IsSuccess.ShouldBeTrue();
        bill.BalanceDue.ShouldBe(Money.Of(0.01m));
        bill.Status.ShouldBe(BillStatus.PartiallyPaid, "one paisa is still owed");

        bill.ApplyCredit(Money.Of(0.01m)).IsSuccess.ShouldBeTrue();
        bill.BalanceDue.ShouldBe(Money.Zero);
        bill.Status.ShouldBe(BillStatus.Settled);
        bill.DomainEvents.Count.ShouldBe(3);
    }

    [Fact]
    public void BR208_the_whole_balance_at_once_settles_a_posted_bill()
    {
        var bill = Posted();

        bill.ApplyCredit(Money.Of(590m)).IsSuccess.ShouldBeTrue();

        bill.Status.ShouldBe(BillStatus.Settled);
        bill.BalanceDue.ShouldBe(Money.Zero);
    }

    [Fact]
    public void BR303_more_than_the_balance_due_is_refused_and_the_bill_stays_as_it_was()
    {
        var bill = Posted();
        bill.ApplyCredit(Money.Of(500m));
        bill.ClearDomainEvents();

        var refused = bill.ApplyCredit(Money.Of(90.01m));

        refused.Error.Code.ShouldBe(BillingErrors.AllocationExceedsBalanceDueCode);
        refused.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        refused.Error.Message.ShouldContain("BIL/26-27/000042");
        refused.Error.Message.ShouldContain("90.00");
        bill.BalanceDue.ShouldBe(Money.Of(90m));
        bill.Status.ShouldBe(BillStatus.PartiallyPaid);
        bill.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void BR303_a_settled_bill_is_owed_nothing_so_any_amount_is_more_than_its_balance()
    {
        var bill = Posted();
        bill.ApplyCredit(Money.Of(590m));

        bill.ApplyCredit(Money.Of(0.01m)).Error.Code.ShouldBe(BillingErrors.AllocationExceedsBalanceDueCode);
        bill.Status.ShouldBe(BillStatus.Settled);
    }

    [Fact]
    public void BR303_a_draft_a_voided_draft_and_a_bill_that_waits_for_approval_are_not_posted_and_refuse()
    {
        var draft = Draft();
        var voided = Draft();
        voided.Void("Raised by mistake", PostedAt, voidedBy: null);
        var waiting = Draft();
        waiting.SubmitForApproval(Window);

        foreach (var bill in new[] { draft, voided, waiting })
        {
            var status = bill.Status;
            bill.ClearDomainEvents();

            var refused = bill.ApplyCredit(Money.Of(1m));

            refused.Error.Code.ShouldBe(BillingErrors.BillNotPostedCode);
            refused.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
            bill.Status.ShouldBe(status);
            bill.BalanceDue.ShouldBe(Money.Of(590m));
            bill.DomainEvents.ShouldBeEmpty();
        }
    }

    [Fact]
    public void An_amount_that_is_not_above_zero_is_a_bug_and_throws()
    {
        var bill = Posted();

        Should.Throw<ArgumentException>(() => bill.ApplyCredit(Money.Zero));
        Should.Throw<ArgumentException>(() => bill.ApplyCredit(Money.Of(-1m)));
        bill.BalanceDue.ShouldBe(Money.Of(590m));
    }

    [Fact]
    public void BR006_applying_an_amount_changes_the_balance_and_the_status_and_nothing_else()
    {
        var bill = Posted();

        bill.ApplyCredit(Money.Of(200m));

        bill.Total.ShouldBe(Money.Of(590m));
        bill.SubTotal.ShouldBe(Money.Of(500m));
        bill.TaxTotal.ShouldBe(Money.Of(90m));
        bill.Number.ShouldBe("BIL/26-27/000042");
        bill.BillDate.ShouldBe(Today);
        bill.PayerId.ShouldBe(Payer);
        bill.PostedAtUtc.ShouldBe(PostedAt);
        bill.Lines.ShouldHaveSingleItem().LineTotal.ShouldBe(Money.Of(590m));

        // And the bill is as frozen as before (BR-006).
        bill.UpdateHeader(new BillHeader { BillDate = Today, DueDate = Today }, Window).Error.Code.ShouldBe(BillingErrors.BillAlreadyPostedCode);
        bill.Void("Changed my mind", PostedAt, voidedBy: null).Error.Code.ShouldBe(BillingErrors.BillAlreadyPostedCode);
    }

    [Fact]
    public void BR208_an_amount_given_back_raises_what_is_owed_and_moves_the_status_back_with_the_balance()
    {
        var bill = Posted();
        bill.ApplyCredit(Money.Of(590m)).IsSuccess.ShouldBeTrue();
        bill.ClearDomainEvents();

        // Settled -> PartiallyPaid: part of it is still settled.
        bill.ReverseCredit(Money.Of(200m)).IsSuccess.ShouldBeTrue();
        (bill.BalanceDue, bill.Status).ShouldBe((Money.Of(200m), BillStatus.PartiallyPaid));
        bill.DomainEvents.ShouldHaveSingleItem().ShouldBe(new BillCreditReversed(bill.Id, "BIL/26-27/000042", Money.Of(200m), Money.Of(200m)));

        // PartiallyPaid -> Posted: nothing is settled any more.
        bill.ReverseCredit(Money.Of(390m)).IsSuccess.ShouldBeTrue();
        (bill.BalanceDue, bill.Status).ShouldBe((Money.Of(590m), BillStatus.Posted));
        bill.Total.ShouldBe(Money.Of(590m), "a posted bill keeps its total (BR-006)");
    }

    [Fact]
    public void BR305_only_what_was_applied_can_be_given_back_and_only_to_a_posted_bill()
    {
        var bill = Posted();
        bill.ApplyCredit(Money.Of(100m)).IsSuccess.ShouldBeTrue();

        // More than was ever applied is a bug in the caller, not a business answer.
        Should.Throw<ArgumentException>(() => bill.ReverseCredit(Money.Of(100.01m)));
        Should.Throw<ArgumentException>(() => bill.ReverseCredit(Money.Zero));
        bill.BalanceDue.ShouldBe(Money.Of(490m));

        Draft().ReverseCredit(Money.Of(1m)).Error.Code.ShouldBe(BillingErrors.BillNotPostedCode);
    }

    private static Bill Posted()
    {
        var bill = Draft();
        bill.Post(DocumentNumber.Create(DocumentType.Bill, FiscalYear.Of(2026), "BIL", 42), PostedAt, postedBy: null, Window).IsSuccess.ShouldBeTrue();

        return bill;
    }

    // 1 x 500.00 at 18%: 500.00 + 90.00.
    private static Bill Draft() => Bill.Create(
        Patient,
        Payer,
        new BillHeader { BillDate = Today, DueDate = Today.AddDays(15) },
        [new BillLineDetails { Description = "OPD consultation", Quantity = Quantity.Of(1m), UnitPrice = UnitPrice.Of(500m), TaxRate = Percent.Of(18m) }],
        Window).Value;
}
