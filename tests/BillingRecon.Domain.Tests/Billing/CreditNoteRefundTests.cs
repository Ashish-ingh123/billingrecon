using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Billing;

/// <summary>
/// A credit note pays a refund out of its available credit (BR-213, BR-307; ADR 0029): only once it is posted, never
/// more than is available, and its applications are untouched.
/// </summary>
public sealed class CreditNoteRefundTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly BusinessDateWindow Window = new(Today, maxBackdateDays: 7);
    private static readonly BillId OwnBill = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3910"));
    private static readonly PayerId Payer = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3902"));
    private static readonly PatientId Patient = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3901"));
    private static readonly UserId Checker = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3921"));
    private static readonly DateTimeOffset PostedAt = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BR213_a_refund_takes_available_credit_and_the_applications_stay()
    {
        var creditNote = Posted();
        creditNote.ApplyToOwnBillAtPosting(Money.Of(26m), PostedAt, Checker).IsSuccess.ShouldBeTrue();
        creditNote.ClearDomainEvents();

        creditNote.PayRefund("REF/26-27/000001", Money.Of(60m)).IsSuccess.ShouldBeTrue();

        creditNote.AvailableAmount.ShouldBe(Money.Of(40m));
        creditNote.Total.ShouldBe(Money.Of(126m), "a posted credit note keeps its total (BR-006)");
        creditNote.Allocations.ShouldHaveSingleItem().Amount.ShouldBe(Money.Of(26m));
        creditNote.DomainEvents.ShouldHaveSingleItem().ShouldBe(new CreditNoteRefunded("CN/26-27/000007", "REF/26-27/000001", Money.Of(60m), Money.Of(40m)));
    }

    [Fact]
    public void BR213_more_than_is_available_and_a_credit_note_that_is_not_posted_are_refused()
    {
        var creditNote = Posted();
        creditNote.ClearDomainEvents();

        creditNote.PayRefund("REF/26-27/000001", Money.Of(126.01m)).Error.Code.ShouldBe(BillingErrors.CreditExceedsAvailableCode);
        creditNote.AvailableAmount.ShouldBe(Money.Of(126m));
        creditNote.DomainEvents.ShouldBeEmpty();
        Draft().PayRefund("REF/26-27/000001", Money.Of(1m)).Error.Code.ShouldBe(BillingErrors.CreditNoteNotPostedCode);
        Should.Throw<ArgumentException>(() => creditNote.PayRefund("REF/26-27/000001", Money.Zero));
        Should.Throw<ArgumentException>(() => creditNote.PayRefund(" ", Money.Of(1m)));
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

    private static CreditNote Posted()
    {
        var creditNote = Draft();
        creditNote.SubmitForApproval(Today, Window).IsSuccess.ShouldBeTrue();
        creditNote.PostApproved(DocumentNumber.Create(DocumentType.CreditNote, FiscalYear.FromDate(Today), "CN", 7), PostedAt, Checker, Today, Window).IsSuccess.ShouldBeTrue();

        return creditNote;
    }
}
