using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Domain.Tests.Payments;

/// <summary>
/// The refund aggregate (US-34; BR-205, BR-301, BR-307, BR-308, BR-604 to BR-606; ADR 0029): one source, the payout
/// rules of a payment, a date not before the source's, a draft that a second person posts under a refund number, and
/// nothing after that.
/// </summary>
public sealed class RefundTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly DateOnly SourceDate = new(2026, 11, 18);
    private static readonly BusinessDateWindow Window = new(Today, maxBackdateDays: 7);
    private static readonly PayerId Payer = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3702"));
    private static readonly PaymentId Payment = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3710"));
    private static readonly CreditNoteId CreditNote = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3711"));
    private static readonly UserId Cashier = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3720"));
    private static readonly UserId Checker = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3721"));
    private static readonly DateTimeOffset PostedAt = new(2026, 11, 20, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public void US34_a_draft_takes_one_source_its_payer_and_the_payout_of_a_payment()
    {
        var fromPayment = Draft(RefundSource.FromPayment(Payment), reference: "  412345678901  ");
        var fromCreditNote = Draft(RefundSource.FromCreditNote(CreditNote), method: PaymentMethod.Cash, reference: null);

        (fromPayment.SourceKind, fromPayment.SourcePaymentId, fromPayment.SourceCreditNoteId).ShouldBe((RefundSourceKind.Payment, Payment, null));
        (fromCreditNote.SourceKind, fromCreditNote.SourcePaymentId, fromCreditNote.SourceCreditNoteId).ShouldBe((RefundSourceKind.CreditNote, null, CreditNote));
        fromPayment.Source.ShouldBe(RefundSource.FromPayment(Payment));
        (fromPayment.PayerId, fromPayment.RefundDate, fromPayment.Amount, fromPayment.Method, fromPayment.Reference).ShouldBe((Payer, Today, Money.Of(250m), PaymentMethod.UPI, "412345678901"));
        (fromPayment.Status, fromPayment.Number, fromPayment.PostingId).ShouldBe((RefundStatus.Draft, null, null));
        fromCreditNote.Reference.ShouldBeNull();
        fromPayment.DomainEvents.ShouldHaveSingleItem().ShouldBe(new RefundDrafted(Payer, RefundSourceKind.Payment, Payment.Value, Money.Of(250m), PaymentMethod.UPI, Today));
    }

    [Fact]
    public void BR205_BR307_the_refund_date_lies_in_the_window_and_not_before_the_date_of_its_source()
    {
        Refund.Create(RefundSource.FromPayment(Payment), Payer, SourceDate, Today.AddDays(1), Money.Of(1m), PaymentMethod.Cash, null, "x", Window).Error.Code.ShouldBe(PaymentsErrors.RefundDateInFutureCode);
        Refund.Create(RefundSource.FromPayment(Payment), Payer, Today.AddDays(-30), Today.AddDays(-8), Money.Of(1m), PaymentMethod.Cash, null, "x", Window).Error.Code.ShouldBe(PaymentsErrors.RefundDateTooOldCode);
        var before = Refund.Create(RefundSource.FromPayment(Payment), Payer, SourceDate, SourceDate.AddDays(-1), Money.Of(1m), PaymentMethod.Cash, null, "x", Window);
        before.Error.Code.ShouldBe(PaymentsErrors.RefundDateBeforeSourceDateCode);
        before.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        Refund.Create(RefundSource.FromPayment(Payment), Payer, SourceDate, SourceDate, Money.Of(1m), PaymentMethod.Cash, null, "x", Window).IsSuccess.ShouldBeTrue("the source's own date is allowed");

        var draft = Draft(RefundSource.FromPayment(Payment));
        draft.Update(SourceDate.AddDays(-1), Money.Of(1m), PaymentMethod.Cash, null, "x", SourceDate, Window).Error.Code.ShouldBe(PaymentsErrors.RefundDateBeforeSourceDateCode);
        draft.RefundDate.ShouldBe(Today);
    }

    [Fact]
    public void BR301_the_payout_follows_the_rules_of_a_payment_and_the_validator_is_trusted_with_them()
    {
        Should.Throw<ArgumentException>(() => Draft(RefundSource.FromPayment(Payment), method: PaymentMethod.Cheque, reference: " "));
        Should.Throw<ArgumentException>(() => Draft(RefundSource.FromPayment(Payment), amount: 0m));
        Should.Throw<ArgumentException>(() => Draft(RefundSource.FromPayment(Payment), amount: Refund.MaxAmount + 0.01m));
        Should.Throw<ArgumentOutOfRangeException>(() => Draft(RefundSource.FromPayment(Payment), method: (PaymentMethod)42));
        Should.Throw<ArgumentException>(() => Draft(RefundSource.FromPayment(Payment), reason: " "));
        Should.Throw<ArgumentException>(() => Draft(default));
        Should.Throw<ArgumentException>(() => RefundSource.FromPayment(default));
        Should.Throw<ArgumentException>(() => RefundSource.FromCreditNote(default));
    }

    [Fact]
    public void A_draft_is_replaced_as_a_whole_and_voided_and_then_never_changes()
    {
        var refund = Draft(RefundSource.FromPayment(Payment));
        refund.ClearDomainEvents();

        refund.Update(SourceDate, Money.Of(200m), PaymentMethod.BankTransfer, "UTR0001", "Overpaid twice", SourceDate, Window).IsSuccess.ShouldBeTrue();
        refund.Update(SourceDate, Money.Of(200m), PaymentMethod.BankTransfer, "UTR0001", "Overpaid twice", SourceDate, Window).IsSuccess.ShouldBeTrue("the same values again change nothing");

        (refund.RefundDate, refund.Amount, refund.Method, refund.Reference, refund.Reason).ShouldBe((SourceDate, Money.Of(200m), PaymentMethod.BankTransfer, "UTR0001", "Overpaid twice"));
        refund.Source.ShouldBe(RefundSource.FromPayment(Payment), "the source never changes");
        refund.DomainEvents.ShouldHaveSingleItem().ShouldBe(new RefundUpdated(refund.Id, Money.Of(200m), PaymentMethod.BankTransfer, SourceDate));

        refund.Void(" Raised twice ", PostedAt, Cashier).IsSuccess.ShouldBeTrue();
        (refund.Status, refund.VoidReason, refund.VoidedAtUtc, refund.VoidedByUserId).ShouldBe((RefundStatus.Voided, "Raised twice", PostedAt, Cashier));
        refund.Update(Today, Money.Of(1m), PaymentMethod.Cash, null, "x", SourceDate, Window).Error.Code.ShouldBe(PaymentsErrors.RefundNotDraftCode);
        refund.Void("x", PostedAt, Cashier).Error.Code.ShouldBe(PaymentsErrors.RefundNotDraftCode);
        refund.SubmitForApproval(SourceDate, Window).Error.Code.ShouldBe(PaymentsErrors.RefundNotDraftCode);
        refund.CheckCanApprove(SourceDate, Window).Error.Code.ShouldBe(PaymentsErrors.RefundNotDraftCode);
    }

    [Fact]
    public void BR604_a_draft_waits_for_a_second_person_who_posts_it_under_a_refund_number_or_returns_it()
    {
        var refund = Draft(RefundSource.FromCreditNote(CreditNote));
        refund.CheckCanApprove(SourceDate, Window).Error.Code.ShouldBe(PaymentsErrors.RefundNotPendingApprovalCode);
        refund.ClearDomainEvents();

        refund.SubmitForApproval(SourceDate, Window).IsSuccess.ShouldBeTrue();
        refund.Status.ShouldBe(RefundStatus.PendingApproval);
        refund.DomainEvents.ShouldHaveSingleItem().ShouldBe(new RefundSubmittedForApproval(refund.Id, Money.Of(250m)));
        refund.Update(Today, Money.Of(1m), PaymentMethod.Cash, null, "x", SourceDate, Window).Error.Code.ShouldBe(PaymentsErrors.RefundPendingApprovalCode);
        refund.Void("x", PostedAt, Cashier).Error.Code.ShouldBe(PaymentsErrors.RefundPendingApprovalCode);

        // Rejected: a draft again, and submitted once more.
        refund.ReturnToDraft(" Wrong amount ").IsSuccess.ShouldBeTrue();
        refund.Status.ShouldBe(RefundStatus.Draft);
        refund.ReturnToDraft("x").Error.Code.ShouldBe(PaymentsErrors.RefundNotPendingApprovalCode);
        refund.SubmitForApproval(SourceDate, Window).IsSuccess.ShouldBeTrue();
        refund.ClearDomainEvents();

        // Approved: the posting, under the number of the fiscal year of the refund date.
        refund.PostApproved(Number(3), PostedAt, Checker, SourceDate, Window).IsSuccess.ShouldBeTrue();
        (refund.Status, refund.Number, refund.SequenceNumber, refund.PostedAtUtc, refund.PostedByUserId).ShouldBe((RefundStatus.Posted, "REF/26-27/000003", 3, PostedAt, Checker));
        refund.FiscalYear.ShouldBe(FiscalYear.FromDate(Today));
        refund.DomainEvents.ShouldHaveSingleItem().ShouldBe(
            new RefundPosted(refund.Id, "REF/26-27/000003", Payer, RefundSourceKind.CreditNote, CreditNote.Value, Money.Of(250m), PaymentMethod.UPI, "Paid twice"));

        // Posted for good: never changed, never decided again, never reversed (BR-308).
        refund.PostApproved(Number(4), PostedAt, Checker, SourceDate, Window).Error.Code.ShouldBe(PaymentsErrors.RefundAlreadyPostedCode);
        refund.ReturnToDraft("x").Error.Code.ShouldBe(PaymentsErrors.RefundAlreadyPostedCode);
        refund.Update(Today, Money.Of(1m), PaymentMethod.Cash, null, "x", SourceDate, Window).Error.Code.ShouldBe(PaymentsErrors.RefundAlreadyPostedCode);
        refund.Void("x", PostedAt, Cashier).Error.Code.ShouldBe(PaymentsErrors.RefundAlreadyPostedCode);
    }

    [Fact]
    public void BR205_a_refund_that_waited_too_long_is_not_approved_and_a_number_of_another_kind_or_year_is_a_bug()
    {
        var refund = Draft(RefundSource.FromPayment(Payment));
        refund.SubmitForApproval(SourceDate, Window).IsSuccess.ShouldBeTrue();

        refund.CheckCanApprove(SourceDate, new BusinessDateWindow(Today.AddDays(30), maxBackdateDays: 7)).Error.Code.ShouldBe(PaymentsErrors.RefundDateTooOldCode);
        Should.Throw<ArgumentException>(() => refund.PostApproved(DocumentNumber.Create(DocumentType.Payment, FiscalYear.FromDate(Today), "PAY", 1), PostedAt, Checker, SourceDate, Window));
        Should.Throw<ArgumentException>(() => refund.PostApproved(DocumentNumber.Create(DocumentType.Refund, FiscalYear.FromDate(Today.AddYears(-1)), "REF", 1), PostedAt, Checker, SourceDate, Window));
        refund.Status.ShouldBe(RefundStatus.PendingApproval);
    }

    [Fact]
    public void BR307_a_refund_fits_what_its_source_has_left()
    {
        var refund = Draft(RefundSource.FromPayment(Payment));

        refund.CheckFits("PAY/26-27/000007", Money.Of(250m)).IsSuccess.ShouldBeTrue("all that is left");
        var refused = refund.CheckFits("PAY/26-27/000007", Money.Of(249.99m));
        refused.Error.Code.ShouldBe(PaymentsErrors.RefundExceedsAvailableCode);
        refused.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
    }

    [Fact]
    public void The_posting_is_recorded_once_and_only_on_a_posted_refund()
    {
        var refund = Draft(RefundSource.FromPayment(Payment));
        var posting = new LedgerPostingId(Guid.NewGuid());

        Should.Throw<InvalidOperationException>(() => refund.AttachPosting(posting));
        refund.SubmitForApproval(SourceDate, Window).IsSuccess.ShouldBeTrue();
        refund.PostApproved(Number(1), PostedAt, Checker, SourceDate, Window).IsSuccess.ShouldBeTrue();

        Should.Throw<ArgumentException>(() => refund.AttachPosting(default));
        refund.AttachPosting(posting);
        refund.PostingId.ShouldBe(posting);
        Should.Throw<InvalidOperationException>(() => refund.AttachPosting(new LedgerPostingId(Guid.NewGuid())));
    }

    private static DocumentNumber Number(int sequence) => DocumentNumber.Create(DocumentType.Refund, FiscalYear.FromDate(Today), "REF", sequence);

    private static Refund Draft(
        RefundSource source,
        decimal amount = 250m,
        PaymentMethod method = PaymentMethod.UPI,
        string? reference = "412345678901",
        string reason = "Paid twice")
        => Refund.Create(source, Payer, SourceDate, Today, Money.Of(amount), method, reference, reason, Window).Value;
}
