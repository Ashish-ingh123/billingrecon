using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Domain.Payers;
using BillingRecon.Domain.Payments;
using BillingRecon.Domain.Receivables;

namespace BillingRecon.Domain.Tests.Receivables;

/// <summary>
/// Opening balances in the domain (ADR 0053): an opening receivable is a posted bill of its own series and an opening
/// advance a posted payment of its own series, each with the number it had before; the batch that brings them is
/// decided once; and the two opening prefixes are fixed.
/// </summary>
public sealed class OpeningBalanceTests
{
    private static readonly DateOnly CutOver = new(2027, 4, 1);
    private static readonly DateTimeOffset Now = new(2027, 4, 2, 5, 0, 0, TimeSpan.Zero);
    private static readonly PatientId Patient = new(Guid.NewGuid());
    private static readonly PayerId Payer = new(Guid.NewGuid());
    private static readonly UserId Checker = new(Guid.NewGuid());
    private const string Hash = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    private static DocumentNumber Number(DocumentType type, string prefix, int sequence = 1)
        => DocumentNumber.Create(type, FiscalYear.FromDate(CutOver), prefix, sequence);

    private static Bill Receivable(decimal outstanding = 1250.50m)
        => Bill.BringForward(Patient, Payer, new DateOnly(2026, 12, 20), new DateOnly(2027, 1, 4), Money.Of(outstanding), " OLD/26-27/00017 ", Number(DocumentType.OpeningReceivable, "OBR"), Now, Checker);

    private static Payment Advance()
        => Payment.BringForward(Payer, new DateOnly(2027, 3, 14), PaymentMethod.Cheque, Money.Of(7903.54m), "ADV-00001", "Opening advance.", Number(DocumentType.OpeningAdvance, "OBA"));

    [Fact]
    public void ADR0053_an_opening_receivable_is_a_posted_bill_of_the_opening_series_with_its_old_number()
    {
        var bill = Receivable();

        bill.Status.ShouldBe(BillStatus.Posted);
        bill.Origin.ShouldBe(DocumentOrigin.Opening);
        bill.LegacyNumber.ShouldBe("OLD/26-27/00017");
        bill.Number.ShouldBe("OBR/27-28/000001");
        (bill.FiscalYear, bill.SequenceNumber).ShouldBe((FiscalYear.FromDate(CutOver), 1));

        // The old dates are kept, though they lie in the fiscal year before the number's: ageing counts from them.
        (bill.BillDate, bill.DueDate).ShouldBe((new DateOnly(2026, 12, 20), new DateOnly(2027, 1, 4)));
        (bill.Total, bill.BalanceDue, bill.TaxTotal).ShouldBe((Money.Of(1250.50m), Money.Of(1250.50m), Money.Zero));
        bill.Lines.ShouldHaveSingleItem().Description.ShouldBe("Opening balance of bill OLD/26-27/00017");
        (bill.PostedAtUtc, bill.PostedByUserId).ShouldBe((Now, Checker));
        bill.DomainEvents.ShouldHaveSingleItem().ShouldBe(
            new BillBroughtForward("OBR/27-28/000001", Payer, Money.Of(1250.50m), new DateOnly(2026, 12, 20), "OLD/26-27/00017"));
    }

    [Fact]
    public void ADR0053_an_opening_receivable_is_settled_like_any_posted_bill()
    {
        var bill = Receivable(1000m);

        bill.ApplyCredit(Money.Of(400m)).IsSuccess.ShouldBeTrue();
        bill.Status.ShouldBe(BillStatus.PartiallyPaid);
        bill.ApplyCredit(Money.Of(600m)).IsSuccess.ShouldBeTrue();
        (bill.Status, bill.BalanceDue).ShouldBe((BillStatus.Settled, Money.Zero));
        bill.ApplyCredit(Money.Of(0.01m)).Error.Code.ShouldBe(BillingErrors.AllocationExceedsBalanceDueCode);
    }

    [Fact]
    public void ADR0053_an_opening_receivable_that_is_not_one_is_a_bug_in_the_caller()
    {
        var date = new DateOnly(2026, 12, 20);
        var number = Number(DocumentType.OpeningReceivable, "OBR");

        Should.Throw<ArgumentException>(() => Bill.BringForward(Patient, Payer, date, date, Money.Of(10m), "OLD-1", Number(DocumentType.Bill, "BIL"), Now, Checker));
        Should.Throw<ArgumentException>(() => Bill.BringForward(Patient, Payer, date, date, Money.Zero, "OLD-1", number, Now, Checker));
        Should.Throw<ArgumentException>(() => Bill.BringForward(Patient, Payer, date, date.AddDays(-1), Money.Of(10m), "OLD-1", number, Now, Checker));
        Should.Throw<ArgumentException>(() => Bill.BringForward(Patient, Payer, date, date, Money.Of(10m), " ", number, Now, Checker));
        Should.Throw<ArgumentException>(() => Bill.BringForward(default, Payer, date, date, Money.Of(10m), "OLD-1", number, Now, Checker));
    }

    [Fact]
    public void ADR0053_an_opening_advance_is_a_posted_payment_of_the_opening_series_with_its_old_reference()
    {
        var payment = Advance();

        payment.Status.ShouldBe(PaymentStatus.Posted);
        payment.Origin.ShouldBe(DocumentOrigin.Opening);
        (payment.LegacyNumber, payment.Reference).ShouldBe(("ADV-00001", "ADV-00001"));
        payment.Number.ShouldBe("OBA/27-28/000001");
        (payment.Amount, payment.UnappliedAmount).ShouldBe((Money.Of(7903.54m), Money.Of(7903.54m)));
        (payment.PaymentDate, payment.Method).ShouldBe((new DateOnly(2027, 3, 14), PaymentMethod.Cheque));
        payment.DomainEvents.ShouldHaveSingleItem().ShouldBe(
            new PaymentBroughtForward("OBA/27-28/000001", Payer, PaymentMethod.Cheque, Money.Of(7903.54m), new DateOnly(2027, 3, 14), "ADV-00001"));
    }

    [Fact]
    public void ADR0053_an_opening_advance_is_allocated_and_refunded_but_never_reversed()
    {
        var payment = Advance();
        payment.AttachPosting(new LedgerPostingId(Guid.NewGuid()));

        payment.Allocate(new BillId(Guid.NewGuid()), Money.Of(903.54m), Now, Checker).IsSuccess.ShouldBeTrue();
        payment.CheckCanRefund().IsSuccess.ShouldBeTrue();

        payment.RequestReversal("Brought in by mistake").Error.Code.ShouldBe(PaymentsErrors.OpeningAdvanceNotReversibleCode);
        payment.Status.ShouldBe(PaymentStatus.Posted);
    }

    [Fact]
    public void ADR0053_an_opening_advance_under_a_payment_number_is_a_bug_in_the_caller()
        => Should.Throw<ArgumentException>(() => Payment.BringForward(Payer, new DateOnly(2027, 3, 14), PaymentMethod.Cash, Money.Of(10m), "ADV-1", null, Number(DocumentType.Payment, "PAY")));

    [Fact]
    public void ADR0053_a_document_raised_here_has_the_standard_origin_and_no_old_number()
    {
        var window = new BusinessDateWindow(new DateOnly(2027, 4, 2), 7);
        var payment = Payment.Record(Payer, new DateOnly(2027, 4, 2), PaymentMethod.Cash, null, Money.Of(10m), null, Number(DocumentType.Payment, "PAY"), window).Value;

        (payment.Origin, payment.LegacyNumber).ShouldBe((DocumentOrigin.Standard, null));
        payment.RequestReversal("Entered twice").IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ADR0053_a_batch_waits_and_is_decided_once()
    {
        var approved = Batch();
        approved.Status.ShouldBe(OpeningBatchStatus.PendingApproval);
        approved.DomainEvents.ShouldHaveSingleItem().ShouldBe(new OpeningBatchSubmitted(CutOver, 2, Money.Of(300m), 1, Money.Of(50m)));

        approved.RecordApproval(Now).IsSuccess.ShouldBeTrue();
        (approved.Status, approved.DecidedAtUtc).ShouldBe((OpeningBatchStatus.Approved, Now));
        approved.RecordApproval(Now).Error.Code.ShouldBe(ReceivablesErrors.OpeningBatchNotPendingCode);
        approved.RecordRejection("Too late", Now).Error.Code.ShouldBe(ReceivablesErrors.OpeningBatchNotPendingCode);

        var rejected = Batch();
        rejected.RecordRejection(" The totals are last month's ", Now).IsSuccess.ShouldBeTrue();
        rejected.Status.ShouldBe(OpeningBatchStatus.Rejected);
        rejected.DomainEvents.Last().ShouldBe(new OpeningBatchRejected(rejected.Id, "The totals are last month's"));
        rejected.RecordApproval(Now).Error.Code.ShouldBe(ReceivablesErrors.OpeningBatchNotPendingCode);
    }

    [Fact]
    public void ADR0053_a_batch_that_is_empty_too_large_or_without_its_hash_is_a_bug_in_the_caller()
    {
        Should.Throw<ArgumentException>(() => OpeningBatch.Submit(CutOver, Hash, "b", "a", "t", 0, Money.Zero, 0, Money.Zero));
        Should.Throw<ArgumentException>(() => OpeningBatch.Submit(CutOver, Hash, "b", "a", "t", OpeningBatch.MaxDocuments, Money.Of(1m), 1, Money.Of(1m)));
        Should.Throw<ArgumentException>(() => OpeningBatch.Submit(CutOver, Hash.ToLowerInvariant(), "b", "a", "t", 1, Money.Of(1m), 0, Money.Zero));
        Should.Throw<ArgumentException>(() => OpeningBatch.Submit(CutOver, "ABC", "b", "a", "t", 1, Money.Of(1m), 0, Money.Zero));
    }

    [Fact]
    public void ADR0053_the_prefixes_of_the_opening_balances_are_fixed_and_no_tenant_takes_them()
    {
        var tenant = Tenant.Create("city-hospital", "City Hospital", Now).Value;

        (tenant.PrefixOf(DocumentType.OpeningReceivable), tenant.PrefixOf(DocumentType.OpeningAdvance)).ShouldBe(("OBR", "OBA"));
        Should.Throw<ArgumentException>(() => tenant.ChangePrefix(DocumentType.OpeningReceivable, "OPN"));
        Should.Throw<InvalidOperationException>(() => tenant.ChangePrefix(DocumentType.Bill, "OBR"));
        Should.Throw<InvalidOperationException>(() => tenant.ChangePrefix(DocumentType.Payment, "OBA"));
    }

    [Fact]
    public void ADR0053_a_guarantor_keeps_the_reference_it_had_before()
    {
        var guarantor = Domain.Payers.Payer.Create(PayerKind.SelfPay, new PayerDetails { Name = "Meera Rao" }, patientId: null, " G-0003 ").Value;
        var made = Domain.Payers.Payer.Create(PayerKind.SelfPay, new PayerDetails { Name = "Meera Rao" }, patientId: null).Value;

        guarantor.LegacyReference.ShouldBe("G-0003");
        made.LegacyReference.ShouldBeNull();
    }

    private static OpeningBatch Batch() => OpeningBatch.Submit(CutOver, Hash, "bills", "advances", "trial", 2, Money.Of(300m), 1, Money.Of(50m));
}
