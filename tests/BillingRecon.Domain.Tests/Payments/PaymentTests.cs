using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Domain.Tests.Payments;

/// <summary>
/// The Payment aggregate (BR-301, BR-302; docs/domain-model.md, section 4.6): recording is posting, so a payment is
/// created with its number and with all of its amount unapplied.
/// </summary>
public sealed class PaymentTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly BusinessDateWindow Window = new(Today, maxBackdateDays: 7);
    private static readonly PayerId Payer = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3302"));
    private static readonly FiscalYear Fy2026 = FiscalYear.Of(2026);

    [Fact]
    public void BR302_a_recorded_payment_is_posted_under_its_number_with_all_of_its_amount_unapplied()
    {
        var payment = Payment.Record(Payer, Today, PaymentMethod.UPI, " 412345678901 ", Money.Of(3000m), " Paid at the counter ", Number(17), Window).Value;

        payment.Status.ShouldBe(PaymentStatus.Posted);
        payment.Number.ShouldBe("PAY/26-27/000017");
        payment.FiscalYear.ShouldBe(Fy2026);
        payment.SequenceNumber.ShouldBe(17);
        payment.PayerId.ShouldBe(Payer);
        payment.PaymentDate.ShouldBe(Today);
        payment.Method.ShouldBe(PaymentMethod.UPI);
        payment.Reference.ShouldBe("412345678901", "text is stored without surrounding white space");
        payment.Amount.ShouldBe(Money.Of(3000m));
        payment.UnappliedAmount.ShouldBe(Money.Of(3000m), "nothing is applied yet (BR-302)");
        payment.Notes.ShouldBe("Paid at the counter");
    }

    [Fact]
    public void Recording_raises_PaymentRecorded_with_the_number_the_payer_the_method_the_amount_and_the_date()
    {
        var payment = Payment.Record(Payer, Today, PaymentMethod.Cash, null, Money.Of(500m), null, Number(1), Window).Value;

        var raised = payment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<PaymentRecorded>();
        raised.ShouldBe(new PaymentRecorded("PAY/26-27/000001", Payer, PaymentMethod.Cash, Money.Of(500m), Today));
        ((IHasDocumentNumber)raised).DocumentNumber.ShouldBe("PAY/26-27/000001");
    }

    [Fact]
    public void A_new_payment_has_no_id_no_tenant_no_posting_and_no_row_version_until_it_is_saved()
    {
        var payment = Cash();

        payment.Id.ShouldBe(default);
        payment.TenantId.ShouldBe(default);
        payment.PostingId.ShouldBe(default);
        payment.RowVersion.ShouldBeEmpty();
    }

    [Fact]
    public void BR301_cash_needs_no_reference_and_a_blank_one_is_stored_as_none()
    {
        Payment.Record(Payer, Today, PaymentMethod.Cash, null, Money.Of(500m), null, Number(1), Window).Value.Reference.ShouldBeNull();
        Payment.Record(Payer, Today, PaymentMethod.Cash, "   ", Money.Of(500m), null, Number(1), Window).Value.Reference.ShouldBeNull();
        Payment.Record(Payer, Today, PaymentMethod.Cash, "Counter 3", Money.Of(500m), null, Number(1), Window).Value.Reference.ShouldBe("Counter 3");
    }

    [Theory]
    [InlineData(PaymentMethod.Card)]
    [InlineData(PaymentMethod.UPI)]
    [InlineData(PaymentMethod.BankTransfer)]
    [InlineData(PaymentMethod.Cheque)]
    public void BR301_every_method_except_cash_needs_a_reference(PaymentMethod method)
    {
        Should.Throw<ArgumentException>(() => Payment.Record(Payer, Today, method, null, Money.Of(500m), null, Number(1), Window)).Message.ShouldContain("BR-301");
        Should.Throw<ArgumentException>(() => Payment.Record(Payer, Today, method, "  ", Money.Of(500m), null, Number(1), Window));

        Payment.Record(Payer, Today, method, "REF-1", Money.Of(500m), null, Number(1), Window).Value.Method.ShouldBe(method);
    }

    [Fact]
    public void BR301_an_amount_of_zero_a_negative_one_and_one_above_the_limit_are_bugs_and_throw()
    {
        foreach (var amount in new[] { 0m, -0.01m, Payment.MaxAmount + 0.01m })
        {
            Should.Throw<ArgumentException>(() => Payment.Record(Payer, Today, PaymentMethod.Cash, null, Money.Of(amount), null, Number(1), Window));
        }

        Payment.Record(Payer, Today, PaymentMethod.Cash, null, Money.Of(0.01m), null, Number(1), Window).IsSuccess.ShouldBeTrue();
        Payment.Record(Payer, Today, PaymentMethod.Cash, null, Money.Of(Payment.MaxAmount), null, Number(1), Window).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-7, null)]
    [InlineData(1, PaymentsErrors.PaymentDateInFutureCode)]
    [InlineData(-8, PaymentsErrors.PaymentDateTooOldCode)]
    public void BR205_the_payment_date_is_not_in_the_future_and_not_older_than_the_tenant_allows(int daysFromToday, string? refusedWith)
    {
        var paymentDate = Today.AddDays(daysFromToday);

        var check = Payment.CheckDate(paymentDate, Window);
        var recorded = Payment.Record(Payer, paymentDate, PaymentMethod.Cash, null, Money.Of(500m), null, Number(1), Window);

        if (refusedWith is null)
        {
            check.IsSuccess.ShouldBeTrue();
            recorded.IsSuccess.ShouldBeTrue();
        }
        else
        {
            check.Error.Code.ShouldBe(refusedWith);
            check.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
            recorded.Error.Code.ShouldBe(refusedWith, "a refused date creates nothing");
        }
    }

    [Fact]
    public void BR004_the_number_belongs_to_the_fiscal_year_of_the_payment_date()
    {
        // Received on 31 March, recorded on 2 April: the payment belongs to the year that ended.
        var march = new DateOnly(2027, 3, 31);
        var onSecondOfApril = new BusinessDateWindow(new DateOnly(2027, 4, 2), 7);

        Should.Throw<ArgumentException>(() => Payment.Record(Payer, march, PaymentMethod.Cash, null, Money.Of(500m), null, DocumentNumber.Create(DocumentType.Payment, FiscalYear.Of(2027), "PAY", 1), onSecondOfApril));
        Should.Throw<ArgumentException>(() => Payment.Record(Payer, march, PaymentMethod.Cash, null, Money.Of(500m), null, DocumentNumber.Create(DocumentType.Bill, Fy2026, "BIL", 1), onSecondOfApril));

        var payment = Payment.Record(Payer, march, PaymentMethod.Cash, null, Money.Of(500m), null, Number(9), onSecondOfApril).Value;
        payment.FiscalYear.ShouldBe(Fy2026);
        payment.Number.ShouldBe("PAY/26-27/000009");
    }

    [Fact]
    public void An_empty_payer_an_unknown_method_and_texts_longer_than_their_columns_are_bugs_and_throw()
    {
        Should.Throw<ArgumentException>(() => Payment.Record(default, Today, PaymentMethod.Cash, null, Money.Of(500m), null, Number(1), Window));
        Should.Throw<ArgumentOutOfRangeException>(() => Payment.Record(Payer, Today, (PaymentMethod)99, "REF", Money.Of(500m), null, Number(1), Window));
        Should.Throw<ArgumentException>(() => Payment.Record(Payer, Today, PaymentMethod.Card, new string('r', Payment.ReferenceMaxLength + 1), Money.Of(500m), null, Number(1), Window));
        Should.Throw<ArgumentException>(() => Payment.Record(Payer, Today, PaymentMethod.Cash, null, Money.Of(500m), new string('n', Payment.NotesMaxLength + 1), Number(1), Window));
    }

    [Fact]
    public void BR006_a_payment_keeps_the_one_ledger_posting_it_was_recorded_with()
    {
        var payment = Cash();
        var posting = new LedgerPostingId(Guid.NewGuid());

        payment.AttachPosting(posting);

        payment.PostingId.ShouldBe(posting);
        Should.Throw<InvalidOperationException>(() => payment.AttachPosting(new LedgerPostingId(Guid.NewGuid())));
        Should.Throw<ArgumentException>(() => Cash().AttachPosting(default));
        payment.PostingId.ShouldBe(posting);
    }

    [Fact]
    public void BR006_nothing_public_changes_the_financial_fields_of_a_payment()
    {
        typeof(Payment).GetProperties().ShouldAllBe(property => property.SetMethod == null || !property.SetMethod.IsPublic);
        typeof(IHardDeletable).IsAssignableFrom(typeof(Payment)).ShouldBeFalse();
    }

    private static DocumentNumber Number(int sequence) => DocumentNumber.Create(DocumentType.Payment, Fy2026, "PAY", sequence);

    private static Payment Cash() => Payment.Record(Payer, Today, PaymentMethod.Cash, null, Money.Of(500m), null, Number(1), Window).Value;
}
