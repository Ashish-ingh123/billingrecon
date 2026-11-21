using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Payments;
using BillingRecon.Application.Receivables;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Domain.Payments;
using BillingRecon.Domain.Receivables;
using NSubstitute;

namespace BillingRecon.Application.Tests.Payments;

/// <summary>
/// The refund use cases with the ports replaced (US-34; BR-306, BR-307, BR-309, BR-603, BR-606; ADR 0029): the maker's
/// post asks the source first and opens the request, the approval is the posting and asks the source and the payer
/// again before the number is taken, a credit note is reached through the Billing module's contract, and a payment
/// that paid a refund is never asked to be reversed.
/// </summary>
public sealed class RefundHandlerTests
{
    private static readonly byte[] CurrentVersion = [0, 0, 0, 0, 0, 0, 7, 209];
    private static readonly PayerId Payer = new(Guid.NewGuid());
    private static readonly UserId Cashier = new(Guid.NewGuid());
    private static readonly UserId Checker = new(Guid.NewGuid());
    private static readonly RefundId Id = new(Guid.NewGuid());
    private static readonly PaymentId PaymentId = new(Guid.NewGuid());
    private static readonly CreditNoteId CreditNoteId = new(Guid.NewGuid());
    private static readonly FiscalYear Fy = FiscalYear.FromDate(TestDoubles.Today);
    private static readonly BusinessDateWindow Window = new(TestDoubles.Today, 7);

    private readonly ITenantSettingsReader _settings = Substitute.For<ITenantSettingsReader>();
    private readonly IPaymentRepository _payments = Substitute.For<IPaymentRepository>();
    private readonly ICreditNoteSettlement _creditNotes = Substitute.For<ICreditNoteSettlement>();
    private readonly IActivePayers _payers = Substitute.For<IActivePayers>();
    private readonly IRefundRepository _repository = Substitute.For<IRefundRepository>();
    private readonly IApprovalRequestRepository _approvals = Substitute.For<IApprovalRequestRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ICorrelationContext _correlation = Substitute.For<ICorrelationContext>();
    private readonly INumberSeries _numbers = Substitute.For<INumberSeries>();
    private readonly IReceivablesLedger _ledger = Substitute.For<IReceivablesLedger>();
    private readonly IClock _clock = TestDoubles.ClockAt(TestDoubles.Today);
    private readonly IUnitOfWork _unitOfWork = TestDoubles.UnitOfWork();
    private readonly List<PostingTemplate> _posted = [];

    public RefundHandlerTests()
    {
        _clock.ToIstDate(Arg.Any<DateTimeOffset>()).Returns(TestDoubles.Today);
        _settings.GetDocumentSettingsAsync(Arg.Any<CancellationToken>()).Returns(new TenantDocumentSettings(MaxBackdateDays: 7, DefaultDueDays: 15));
        _currentUser.UserId.Returns(Checker);
        _correlation.CorrelationId.Returns(Guid.NewGuid());
        _payers.EnsureActiveAsync(Payer, Arg.Any<CancellationToken>()).Returns(Result.Success());
        _numbers.NextAsync(DocumentType.Refund, Fy, Arg.Any<CancellationToken>()).Returns(DocumentNumber.Create(DocumentType.Refund, Fy, "REF", 4));
        _ledger.PostAsync(Arg.Do<PostingTemplate>(_posted.Add), Arg.Any<CancellationToken>()).Returns(_ => (Result<LedgerPostingId>)new LedgerPostingId(Guid.NewGuid()));
        _creditNotes.FindPostedAsync(CreditNoteId, Arg.Any<CancellationToken>()).Returns((Result<PostedCreditNote>)new PostedCreditNote(Payer, TestDoubles.Today, "CN/26-27/000002", Money.Of(126m)));
        _creditNotes.PayRefundAsync(CreditNoteId, Payer, Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<CancellationToken>()).Returns((Result<SettledCreditNote>)new SettledCreditNote("CN/26-27/000002", Money.Of(26m)));
    }

    [Fact]
    public async Task US34_the_approval_posts_a_refund_from_a_payment_lowers_it_and_decides_the_request_in_one_commit()
    {
        var payment = Recorded();
        var (refund, request) = Waiting(RefundSource.FromPayment(PaymentId));

        var result = await Approve().HandleAsync(new ApproveRefundCommand(Id, Version(), " Bank details checked "), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        (result.Value.Status, result.Value.Number).ShouldBe((RefundStatus.Posted, $"REF/{Fy.ShortLabel}/000004"));
        refund.PostedByUserId.ShouldBe(Checker, "the approval is the posting, so the checker is who posted");
        refund.PostingId.ShouldNotBeNull();
        payment.UnappliedAmount.ShouldBe(Money.Of(150m));
        (request.Status, request.CheckerUserId, request.CheckerComment).ShouldBe((ApprovalStatus.Approved, Checker, "Bank details checked"));

        var template = _posted.ShouldHaveSingleItem();
        (template.Kind, template.DocumentType, template.DocumentId, template.PayerId, template.BusinessDate).ShouldBe((LedgerPostingKind.RefundPosted, LedgerDocumentType.Refund, Id.Value, Payer, TestDoubles.Today));
        template.Description.ShouldBe($"Refund REF/{Fy.ShortLabel}/000004 from payment {payment.Number}");
        template.Lines.Select(line => line.Account).ShouldBe([LedgerAccount.UnappliedCash, LedgerAccount.Cash]);

        Received.InOrder(() =>
        {
            _repository.GetAsync(Id, Arg.Any<CancellationToken>());
            _payments.GetAsync(PaymentId, Arg.Any<CancellationToken>());
            _approvals.GetPendingAsync(ApprovalDocumentType.Refund, Id.Value, Arg.Any<CancellationToken>());
            _payers.EnsureActiveAsync(Payer, Arg.Any<CancellationToken>());
            _numbers.NextAsync(DocumentType.Refund, Fy, Arg.Any<CancellationToken>());
            _ledger.PostAsync(Arg.Any<PostingTemplate>(), Arg.Any<CancellationToken>());
            _unitOfWork.CommitAsync(Arg.Any<CancellationToken>());
        });
        await _creditNotes.DidNotReceiveWithAnyArgs().PayRefundAsync(default, default, default!, default, default);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BR213_the_approval_of_a_refund_from_a_credit_note_reaches_it_through_the_billing_contract()
    {
        Waiting(RefundSource.FromCreditNote(CreditNoteId));

        var result = await Approve().HandleAsync(new ApproveRefundCommand(Id, Version(), null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _creditNotes.Received(1).PayRefundAsync(CreditNoteId, Payer, $"REF/{Fy.ShortLabel}/000004", Money.Of(100m), Arg.Any<CancellationToken>());
        var template = _posted.ShouldHaveSingleItem();
        template.Description.ShouldBe($"Refund REF/{Fy.ShortLabel}/000004 from credit note CN/26-27/000002");
        template.Lines.Select(line => line.Account).ShouldBe([LedgerAccount.CreditAvailable, LedgerAccount.Cash]);
        await _payments.DidNotReceiveWithAnyArgs().GetAsync(default, default);
    }

    [Fact]
    public async Task BR307_a_source_that_no_longer_has_the_amount_is_refused_at_the_approval_before_a_number_is_taken()
    {
        var payment = Recorded();
        payment.Allocate(new BillId(Guid.NewGuid()), Money.Of(200m), _clock.UtcNow, Cashier).IsSuccess.ShouldBeTrue();
        Waiting(RefundSource.FromPayment(PaymentId));

        var result = await Approve().HandleAsync(new ApproveRefundCommand(Id, Version(), null), CancellationToken.None);

        result.Error.Code.ShouldBe(PaymentsErrors.RefundExceedsAvailableCode);
        payment.UnappliedAmount.ShouldBe(Money.Of(50m));
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task BR309_a_payer_deactivated_while_the_refund_waited_is_refused_at_the_approval_before_a_number_is_taken()
    {
        Recorded();
        Waiting(RefundSource.FromPayment(PaymentId));
        _payers.EnsureActiveAsync(Payer, Arg.Any<CancellationToken>()).Returns(PayersErrors.PayerInactive());

        var result = await Approve().HandleAsync(new ApproveRefundCommand(Id, Version(), null), CancellationToken.None);

        result.Error.Code.ShouldBe(PayersErrors.PayerInactiveCode);
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task BR603_the_maker_cannot_approve_their_own_refund_and_no_number_is_taken()
    {
        Recorded();
        var (refund, request) = Waiting(RefundSource.FromPayment(PaymentId));
        _currentUser.UserId.Returns(Cashier);

        var result = await Approve().HandleAsync(new ApproveRefundCommand(Id, Version(), null), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ApprovalCheckerIsMakerCode);
        result.Error.Kind.ShouldBe(ErrorKind.Forbidden);
        refund.Status.ShouldBe(RefundStatus.PendingApproval);
        request.Status.ShouldBe(ApprovalStatus.Pending);
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task BR804_a_ledger_that_finds_a_balance_going_below_zero_is_answered_as_a_conflict()
    {
        Recorded();
        Waiting(RefundSource.FromPayment(PaymentId));
        _ledger.PostAsync(Arg.Any<PostingTemplate>(), Arg.Any<CancellationToken>()).Returns((Result<LedgerPostingId>)ReceivablesErrors.LedgerBalanceNegative());

        var result = await Approve().HandleAsync(new ApproveRefundCommand(Id, Version(), null), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task A_stale_version_an_unknown_refund_a_draft_and_a_payment_that_waits_for_its_reversal_are_refused_before_anything_else()
    {
        var payment = Recorded();
        Waiting(RefundSource.FromPayment(PaymentId));

        var stale = await Approve().HandleAsync(new ApproveRefundCommand(Id, Convert.ToBase64String([0, 0, 0, 0, 0, 0, 7, 208]), null), CancellationToken.None);
        var unknown = await Approve().HandleAsync(new ApproveRefundCommand(new RefundId(Guid.NewGuid()), Version(), null), CancellationToken.None);
        payment.RequestReversal("Cheque dishonoured").IsSuccess.ShouldBeTrue();
        var reversing = await Approve().HandleAsync(new ApproveRefundCommand(Id, Version(), null), CancellationToken.None);
        _repository.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(Draft(RefundSource.FromCreditNote(CreditNoteId)));
        var draft = await Approve().HandleAsync(new ApproveRefundCommand(Id, Version(), null), CancellationToken.None);

        stale.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        unknown.Error.Code.ShouldBe(PaymentsErrors.RefundNotFoundCode);
        reversing.Error.Code.ShouldBe(PaymentsErrors.PaymentPendingReversalCode);
        draft.Error.Code.ShouldBe(PaymentsErrors.RefundNotPendingApprovalCode);
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task BR604_the_makers_post_asks_the_source_first_and_opens_the_request_without_anything_financial()
    {
        Recorded();
        var refund = Draft(RefundSource.FromPayment(PaymentId));
        _repository.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(refund);
        _currentUser.UserId.Returns(Cashier);
        ApprovalRequest? opened = null;
        _approvals.Add(Arg.Do<ApprovalRequest>(request => opened = request));

        var result = await Post().HandleAsync(new PostRefundCommand(Id, Version()), CancellationToken.None);

        result.Value.Status.ShouldBe(RefundStatus.PendingApproval);
        result.Value.Number.ShouldBeNull();
        opened.ShouldNotBeNull();
        (opened.DocumentType, opened.DocumentId, opened.Action, opened.MakerUserId).ShouldBe((ApprovalDocumentType.Refund, Id.Value, ApprovalAction.Post, Cashier));
        await NothingWasTakenAsync();
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BR307_the_makers_post_of_more_than_the_source_has_left_is_refused_and_nothing_is_opened()
    {
        var payment = Recorded();
        payment.Allocate(new BillId(Guid.NewGuid()), Money.Of(201m), _clock.UtcNow, Cashier).IsSuccess.ShouldBeTrue();
        _repository.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(Draft(RefundSource.FromPayment(PaymentId)));

        var result = await Post().HandleAsync(new PostRefundCommand(Id, Version()), CancellationToken.None);

        result.Error.Code.ShouldBe(PaymentsErrors.RefundExceedsAvailableCode);
        _approvals.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task BR309_a_draft_is_refused_for_an_inactive_payer_and_takes_the_payer_and_date_of_its_source()
    {
        _payers.EnsureActiveAsync(Payer, Arg.Any<CancellationToken>()).Returns(PayersErrors.PayerInactive());
        var command = new CreateRefundDraftCommand { CreditNoteId = CreditNoteId, Amount = 10m, Method = PaymentMethod.Cash, Reason = "Overpaid" };

        var refused = await Create().HandleAsync(command, CancellationToken.None);
        _payers.EnsureActiveAsync(Payer, Arg.Any<CancellationToken>()).Returns(Result.Success());
        var created = await Create().HandleAsync(command, CancellationToken.None);

        refused.Error.Code.ShouldBe(PayersErrors.PayerInactiveCode);
        (created.Value.PayerId, created.Value.CreditNoteId, created.Value.PaymentId, created.Value.RefundDate).ShouldBe((Payer.Value, CreditNoteId.Value, null, TestDoubles.Today));
        _repository.Received(1).Add(Arg.Any<Refund>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BR306_a_payment_from_which_a_refund_was_posted_is_not_asked_to_be_reversed()
    {
        var payment = Recorded();
        _repository.AnyPostedFromPaymentAsync(PaymentId, Arg.Any<CancellationToken>()).Returns(true);
        _currentUser.UserId.Returns(Cashier);
        var handler = new RequestPaymentReversalHandler(
            new RequestPaymentReversalCommandValidator(), _clock, _currentUser, _correlation, _payments, _repository, _approvals, _unitOfWork);

        var result = await handler.HandleAsync(new RequestPaymentReversalCommand(PaymentId, Version(), "Cheque dishonoured"), CancellationToken.None);

        result.Error.Code.ShouldBe(PaymentsErrors.PaymentRefundedCode);
        result.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        payment.Status.ShouldBe(PaymentStatus.Posted);
        _approvals.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    private ApproveRefundHandler Approve() => new(
        new ApproveRefundCommandValidator(), _clock, _currentUser, _payments, _creditNotes, _payers, _repository, _approvals, _settings, _numbers, _ledger, _unitOfWork);

    private PostRefundHandler Post() => new(_clock, _currentUser, _correlation, _payments, _creditNotes, _repository, _approvals, _settings, _unitOfWork);

    private CreateRefundDraftHandler Create() => new(new CreateRefundDraftCommandValidator(), _settings, _clock, _payments, _creditNotes, _payers, _repository, _unitOfWork);

    private static string Version() => Convert.ToBase64String(CurrentVersion);

    private async Task NothingWasTakenAsync()
    {
        await _numbers.DidNotReceiveWithAnyArgs().NextAsync(default, default, default);
        await _ledger.DidNotReceiveWithAnyArgs().PostAsync(default!, default);
        await _creditNotes.DidNotReceiveWithAnyArgs().PayRefundAsync(default, default, default!, default, default);
    }

    private async Task NothingWasTakenOrWrittenAsync()
    {
        await NothingWasTakenAsync();
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    // A cheque of 250.00 of the payer, nothing allocated yet.
    private Payment Recorded()
    {
        var number = DocumentNumber.Create(DocumentType.Payment, Fy, "PAY", 7);
        var payment = Payment.Record(Payer, TestDoubles.Today, PaymentMethod.Cheque, "000123", Money.Of(250m), notes: null, number, Window).Value.WithId(PaymentId).Saved(CurrentVersion);
        _payments.GetAsync(PaymentId, Arg.Any<CancellationToken>()).Returns(payment);

        return payment;
    }

    // A draft of 100.00 back to the payer by UPI.
    private static Refund Draft(RefundSource source)
        => Refund.Create(source, Payer, TestDoubles.Today, TestDoubles.Today, Money.Of(100m), PaymentMethod.UPI, "412345678901", "Paid twice", Window).Value.WithId(Id).Saved(CurrentVersion);

    // The refund of these tests, waiting, with its pending request of the cashier.
    private (Refund Refund, ApprovalRequest Request) Waiting(RefundSource source)
    {
        var refund = Draft(source);
        refund.SubmitForApproval(TestDoubles.Today, Window).IsSuccess.ShouldBeTrue();
        var request = ApprovalRequest.Open(ApprovalDocumentType.Refund, Id.Value, ApprovalAction.Post, Cashier, makerComment: null, _clock.UtcNow, Guid.NewGuid());
        _repository.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(refund);
        _approvals.GetPendingAsync(ApprovalDocumentType.Refund, Id.Value, Arg.Any<CancellationToken>()).Returns(request);

        return (refund, request);
    }
}
