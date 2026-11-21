using BillingRecon.Application.Billing;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using NSubstitute;

namespace BillingRecon.Application.Tests.Billing;

/// <summary>
/// What Billing answers to a module that settles bills (BR-303, BR-208): the bill exists, is owed by the payer the
/// money came from, is posted, and is owed at least the amount.
/// </summary>
public sealed class BillSettlementTests
{
    private static readonly PatientId Patient = new(Guid.NewGuid());
    private static readonly PayerId Payer = new(Guid.NewGuid());
    private static readonly BillId Id = new(Guid.NewGuid());
    private static readonly BusinessDateWindow Window = new(TestDoubles.Today, 7);

    private readonly IBillRepository _repository = Substitute.For<IBillRepository>();

    [Fact]
    public async Task BR303_an_amount_is_applied_to_a_posted_bill_of_the_payer_and_the_answer_names_the_bill_and_its_balance()
    {
        var bill = Stored(Posted());

        var result = await new BillSettlement(_repository).ApplyAsync(Id, Payer, Money.Of(100m), CancellationToken.None);

        result.Value.ShouldBe(new SettledBill("BIL/26-27/000042", Money.Of(26m)));
        bill.BalanceDue.ShouldBe(Money.Of(26m));
        bill.Status.ShouldBe(BillStatus.PartiallyPaid);
    }

    [Fact]
    public async Task A_bill_the_tenant_does_not_have_is_not_found()
    {
        var result = await new BillSettlement(_repository).ApplyAsync(Id, Payer, Money.Of(100m), CancellationToken.None);

        result.Error.Code.ShouldBe(BillingErrors.BillNotFoundCode);
    }

    [Fact]
    public async Task BR303_a_bill_owed_by_another_payer_is_refused_and_stays_as_it_was()
    {
        var bill = Stored(Posted());

        var result = await new BillSettlement(_repository).ApplyAsync(Id, new PayerId(Guid.NewGuid()), Money.Of(100m), CancellationToken.None);

        result.Error.Code.ShouldBe(BillingErrors.AllocationPayerMismatchCode);
        result.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        bill.BalanceDue.ShouldBe(Money.Of(126m));
        bill.Status.ShouldBe(BillStatus.Posted);
    }

    [Fact]
    public async Task BR303_a_draft_and_an_amount_above_the_balance_are_refused_with_the_bills_own_answer()
    {
        Stored(Draft());
        (await new BillSettlement(_repository).ApplyAsync(Id, Payer, Money.Of(100m), CancellationToken.None)).Error.Code.ShouldBe(BillingErrors.BillNotPostedCode);

        Stored(Posted());
        (await new BillSettlement(_repository).ApplyAsync(Id, Payer, Money.Of(126.01m), CancellationToken.None)).Error.Code.ShouldBe(BillingErrors.AllocationExceedsBalanceDueCode);
    }

    [Fact]
    public async Task BR501_a_posted_bill_is_described_by_its_payer_its_date_its_number_and_what_it_is_owed_and_is_not_changed()
    {
        var bill = Stored(Posted());
        bill.ApplyCredit(Money.Of(100m)).IsSuccess.ShouldBeTrue();
        bill.ClearDomainEvents();

        var result = await new BillSettlement(_repository).FindPostedAsync(Id, CancellationToken.None);

        result.Value.ShouldBe(new PostedBill(Payer, TestDoubles.Today, "BIL/26-27/000042", Money.Of(26m)));
        bill.DomainEvents.ShouldBeEmpty();
        bill.BalanceDue.ShouldBe(Money.Of(26m));
    }

    [Fact]
    public async Task BR501_a_bill_nobody_has_is_not_found_and_a_draft_is_not_posted()
    {
        (await new BillSettlement(_repository).FindPostedAsync(Id, CancellationToken.None)).Error.Code.ShouldBe(BillingErrors.BillNotFoundCode);

        Stored(Draft());
        (await new BillSettlement(_repository).FindPostedAsync(Id, CancellationToken.None)).Error.Code.ShouldBe(BillingErrors.BillNotPostedCode);
    }

    private Bill Stored(Bill bill)
    {
        bill.WithId(Id);
        _repository.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(bill);

        return bill;
    }

    private static Bill Posted()
    {
        var bill = Draft();
        var number = DocumentNumber.Create(DocumentType.Bill, FiscalYear.FromDate(TestDoubles.Today), "BIL", 42);
        bill.Post(number, DateTimeOffset.UnixEpoch, postedBy: null, Window).IsSuccess.ShouldBeTrue();

        return bill;
    }

    // 2 x 60.00 at 5%: 120.00 + 6.00.
    private static Bill Draft() => Bill.Create(
        Patient,
        Payer,
        new BillHeader { BillDate = TestDoubles.Today, DueDate = TestDoubles.Today.AddDays(15) },
        [new BillLineDetails { Description = "Dressing", Quantity = Quantity.Of(2m), UnitPrice = UnitPrice.Of(60m), TaxRate = Percent.Of(5m) }],
        Window).Value;
}
