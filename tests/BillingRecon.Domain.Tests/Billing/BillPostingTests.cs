using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Billing;

/// <summary>
/// Posting a draft bill (BR-206, BR-208, BR-006; docs/domain-model.md, section 5.1): what a draft needs to be posted,
/// what posting sets, and that a posted bill refuses every change.
/// </summary>
public sealed class BillPostingTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly BusinessDateWindow Window = new(Today, maxBackdateDays: 7);
    private static readonly PatientId Patient = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301"));
    private static readonly PayerId Payer = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3302"));
    private static readonly UserId Clerk = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3304"));
    private static readonly DateTimeOffset PostedAt = new(2026, 11, 20, 9, 15, 0, TimeSpan.Zero);
    private static readonly FiscalYear Fy2026 = FiscalYear.Of(2026);

    [Fact]
    public void US21_posting_a_draft_gives_it_its_number_freezes_it_and_leaves_the_whole_total_due()
    {
        var bill = Draft(Line());
        var number = Number(123);

        var result = bill.Post(number, PostedAt, Clerk, Window);

        result.IsSuccess.ShouldBeTrue();
        bill.Status.ShouldBe(BillStatus.Posted);
        bill.Number.ShouldBe("BIL/26-27/000123");
        bill.FiscalYear.ShouldBe(Fy2026);
        bill.SequenceNumber.ShouldBe(123);
        bill.PostedAtUtc.ShouldBe(PostedAt);
        bill.PostedByUserId.ShouldBe(Clerk);
        bill.Total.ShouldBe(Money.Of(590m));
        bill.BalanceDue.ShouldBe(bill.Total, "nothing is paid yet (BR-206)");
        bill.VoidedAtUtc.ShouldBeNull();
    }

    [Fact]
    public void Posting_raises_BillPosted_with_the_number_the_payer_the_total_and_the_date()
    {
        var bill = Draft(Line());
        bill.ClearDomainEvents();

        bill.Post(Number(1), PostedAt, Clerk, Window);

        var raised = bill.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<BillPosted>();
        raised.ShouldBe(new BillPosted(bill.Id, "BIL/26-27/000001", Payer, Money.Of(590m), Today));
        ((IHasDocumentNumber)raised).DocumentNumber.ShouldBe("BIL/26-27/000001");
    }

    [Fact]
    public void BR206_a_bill_that_comes_to_nothing_is_settled_the_moment_it_is_posted()
    {
        var bill = Draft(Line() with { DiscountAmount = Money.Of(500m), TaxRate = Percent.Of(0m) });

        bill.Post(Number(1), PostedAt, Clerk, Window).IsSuccess.ShouldBeTrue();

        bill.Total.ShouldBe(Money.Zero);
        bill.Status.ShouldBe(BillStatus.Settled);
        bill.BalanceDue.ShouldBe(Money.Zero);
        bill.Number.ShouldBe("BIL/26-27/000001");
    }

    [Fact]
    public void A_draft_without_a_line_cannot_be_posted()
    {
        var bill = Draft();

        bill.CheckCanPost(Window).Error.Code.ShouldBe(BillingErrors.BillHasNoLinesCode);
        var result = bill.Post(Number(1), PostedAt, Clerk, Window);

        result.Error.Code.ShouldBe(BillingErrors.BillHasNoLinesCode);
        result.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        bill.Status.ShouldBe(BillStatus.Draft);
        bill.Number.ShouldBeNull();
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    public void BR205_the_bill_date_is_checked_again_on_the_day_of_posting(int daysSinceBillDate, bool allowed)
    {
        // The draft was written on its bill date; the clerk posts it some days later.
        var bill = Draft(Line());
        var onPostingDay = new BusinessDateWindow(Today.AddDays(daysSinceBillDate), maxBackdateDays: 7);

        var check = bill.CheckCanPost(onPostingDay);

        check.IsSuccess.ShouldBe(allowed);
        if (!allowed)
        {
            check.Error.Code.ShouldBe(BillingErrors.BillDateTooOldCode);
            bill.Post(Number(1), PostedAt, Clerk, onPostingDay).IsFailure.ShouldBeTrue();
            bill.Status.ShouldBe(BillStatus.Draft);
        }
    }

    [Fact]
    public void BR205_a_bill_dated_after_the_day_of_posting_cannot_be_posted()
    {
        var bill = Draft(Line());

        bill.CheckCanPost(new BusinessDateWindow(Today.AddDays(-1), 7)).Error.Code.ShouldBe(BillingErrors.BillDateInFutureCode);
    }

    [Fact]
    public void A_voided_bill_cannot_be_posted()
    {
        var bill = Draft(Line());
        bill.Void("Duplicate", PostedAt, Clerk);

        bill.Post(Number(1), PostedAt, Clerk, Window).Error.Code.ShouldBe(BillingErrors.BillNotDraftCode);
        bill.Status.ShouldBe(BillStatus.Voided);
        bill.Number.ShouldBeNull();
    }

    [Fact]
    public void BR006_a_posted_bill_refuses_edit_void_and_a_second_posting_and_stays_exactly_as_it_was()
    {
        var bill = Draft(Line());
        bill.Post(Number(7), PostedAt, Clerk, Window);
        bill.ClearDomainEvents();

        bill.UpdateHeader(new BillHeader { BillDate = Today, DueDate = Today, Notes = "Changed" }, Window).Error.Code.ShouldBe(BillingErrors.BillAlreadyPostedCode);
        bill.ReplaceLines([]).Error.Code.ShouldBe(BillingErrors.BillAlreadyPostedCode);
        bill.Void("Changed my mind", PostedAt.AddHours(1), Clerk).Error.Code.ShouldBe(BillingErrors.BillAlreadyPostedCode);
        bill.Post(Number(8), PostedAt.AddHours(1), Clerk, Window).Error.Code.ShouldBe(BillingErrors.BillAlreadyPostedCode);

        bill.Status.ShouldBe(BillStatus.Posted);
        bill.Number.ShouldBe("BIL/26-27/000007");
        bill.SequenceNumber.ShouldBe(7);
        bill.PostedAtUtc.ShouldBe(PostedAt);
        bill.Notes.ShouldBeNull();
        bill.Lines.ShouldHaveSingleItem();
        bill.Total.ShouldBe(Money.Of(590m));
        bill.VoidReason.ShouldBeNull();
        bill.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void A_number_of_another_document_type_or_of_another_fiscal_year_is_a_bug_and_throws()
    {
        var bill = Draft(Line());

        Should.Throw<ArgumentException>(() => bill.Post(DocumentNumber.Create(DocumentType.Payment, Fy2026, "PAY", 1), PostedAt, Clerk, Window));
        Should.Throw<ArgumentException>(() => bill.Post(DocumentNumber.Create(DocumentType.Bill, FiscalYear.Of(2025), "BIL", 1), PostedAt, Clerk, Window));
        bill.Status.ShouldBe(BillStatus.Draft);
        bill.Number.ShouldBeNull();
    }

    [Fact]
    public void BR004_the_number_belongs_to_the_fiscal_year_of_the_bill_date_not_of_the_day_of_posting()
    {
        // Dated 31 March, posted on 2 April: the bill belongs to the year that ended.
        var billDate = new DateOnly(2027, 3, 31);
        var bill = Bill.Create(Patient, Payer, new BillHeader { BillDate = billDate, DueDate = billDate }, [Line()], new BusinessDateWindow(billDate, 7)).Value;
        var onSecondOfApril = new BusinessDateWindow(new DateOnly(2027, 4, 2), 7);

        Should.Throw<ArgumentException>(() => bill.Post(DocumentNumber.Create(DocumentType.Bill, FiscalYear.Of(2027), "BIL", 1), PostedAt, Clerk, onSecondOfApril));
        bill.Post(DocumentNumber.Create(DocumentType.Bill, Fy2026, "BIL", 9), PostedAt, Clerk, onSecondOfApril).IsSuccess.ShouldBeTrue();

        bill.FiscalYear.ShouldBe(Fy2026);
        bill.Number.ShouldBe("BIL/26-27/000009");
    }

    private static DocumentNumber Number(int sequence) => DocumentNumber.Create(DocumentType.Bill, Fy2026, "BIL", sequence);

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
