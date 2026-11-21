using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Receivables;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Receivables;
using NSubstitute;

namespace BillingRecon.Application.Tests.Billing;

/// <summary>
/// Reversing an application of credit, with the ports replaced (ADR 0028; BR-603, BR-604, BR-605, BR-606): the
/// request only marks the application and opens the approval request, the approval is the reversal, the bill is
/// reached through <see cref="IBillSettlement"/>, and every refusal comes before anything is written.
/// </summary>
public sealed class CreditApplicationReversalHandlerTests
{
    private static readonly byte[] CurrentVersion = [0, 0, 0, 0, 0, 0, 7, 209];
    private static readonly PatientId Patient = new(Guid.NewGuid());
    private static readonly PayerId Payer = new(Guid.NewGuid());
    private static readonly UserId Cashier = new(Guid.NewGuid());
    private static readonly UserId Checker = new(Guid.NewGuid());
    private static readonly CreditNoteId Id = new(Guid.NewGuid());
    private static readonly BillId OwnBill = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));
    private static readonly BillId OtherBill = new(Guid.Parse("00000000-0000-0000-0000-000000000002"));
    private static readonly Guid Correlation = Guid.NewGuid();
    private static readonly BusinessDateWindow Window = new(TestDoubles.Today, 7);

    private readonly ICreditNoteRepository _repository = Substitute.For<ICreditNoteRepository>();
    private readonly IApprovalRequestRepository _approvals = Substitute.For<IApprovalRequestRepository>();
    private readonly IBillSettlement _bills = Substitute.For<IBillSettlement>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ICorrelationContext _correlation = Substitute.For<ICorrelationContext>();
    private readonly IReceivablesLedger _ledger = Substitute.For<IReceivablesLedger>();
    private readonly IClock _clock = TestDoubles.ClockAt(TestDoubles.Today);
    private readonly IUnitOfWork _unitOfWork = TestDoubles.UnitOfWork();
    private readonly List<PostingTemplate> _posted = [];

    public CreditApplicationReversalHandlerTests()
    {
        _clock.ToIstDate(Arg.Any<DateTimeOffset>()).Returns(TestDoubles.Today);
        _currentUser.UserId.Returns(Checker);
        _correlation.CorrelationId.Returns(Correlation);
        _ledger.PostAsync(Arg.Do<PostingTemplate>(_posted.Add), Arg.Any<CancellationToken>()).Returns(_ => (Result<LedgerPostingId>)new LedgerPostingId(Guid.NewGuid()));
        _bills.RestoreAsync(OtherBill, Payer, Arg.Any<Money>(), Arg.Any<CancellationToken>()).Returns((Result<SettledBill>)new SettledBill("BIL/26-27/000002", Money.Of(60m)));
    }

    [Fact]
    public async Task ADR0028_the_request_marks_the_application_and_opens_the_approval_request_in_one_commit()
    {
        var (_, _, later) = Applied();
        _currentUser.UserId.Returns(Cashier);
        ApprovalRequest? opened = null;
        _approvals.Add(Arg.Do<ApprovalRequest>(request => opened = request));

        var result = await Request().HandleAsync(new RequestCreditApplicationReversalCommand(Id, later.Id, Version(), " Applied to the wrong bill "), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Applications.Single(application => application.Id == later.Id.Value).Status.ShouldBe(CreditNoteAllocationStatus.PendingReversal);
        result.Value.AvailableAmount.ShouldBe(40.00m, "nothing moved yet");
        opened.ShouldNotBeNull();
        (opened.DocumentType, opened.DocumentId, opened.Action).ShouldBe((ApprovalDocumentType.CreditNoteAllocation, later.Id.Value, ApprovalAction.Reverse));
        (opened.MakerUserId, opened.MakerComment, opened.CorrelationId).ShouldBe((Cashier, "Applied to the wrong bill", Correlation));
        await NothingWasPostedAsync();
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADR0028_the_application_made_at_posting_is_refused_and_nothing_is_opened()
    {
        var (_, atPosting, _) = Applied();

        var result = await Request().HandleAsync(new RequestCreditApplicationReversalCommand(Id, atPosting.Id, Version(), "Wrong bill"), CancellationToken.None);

        result.Error.Code.ShouldBe(BillingErrors.CreditApplicationMadeAtPostingCode);
        result.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        _approvals.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task ADR0028_the_approval_reverses_the_application_gives_the_amount_back_and_decides_the_request_in_one_commit()
    {
        var (creditNote, atPosting, later) = Applied();
        creditNote.RequestApplicationReversal(later.Id, "Applied to the wrong bill").IsSuccess.ShouldBeTrue();
        var request = Pending(later.Id, "Applied to the wrong bill");

        var result = await Approve().HandleAsync(new ApproveCreditApplicationReversalCommand(Id, later.Id, Version(), " Seen the bill "), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AvailableAmount.ShouldBe(100.00m);
        var reversed = result.Value.Applications.Single(application => application.Id == later.Id.Value);
        (reversed.Status, reversed.ReversalReason, reversed.ReversedAtUtc).ShouldBe((CreditNoteAllocationStatus.Reversed, "Applied to the wrong bill", _clock.UtcNow));
        later.ReversedByUserId.ShouldBe(Checker);
        atPosting.Status.ShouldBe(CreditNoteAllocationStatus.Active);
        (request.Status, request.CheckerUserId, request.CheckerComment).ShouldBe((ApprovalStatus.Approved, Checker, "Seen the bill"));

        // One mirror pair, naming the posting of the application it reverses.
        var template = _posted.ShouldHaveSingleItem();
        (template.Kind, template.DocumentType, template.DocumentId, template.ReversalOf).ShouldBe(
            (LedgerPostingKind.CreditApplicationReversed, LedgerDocumentType.CreditNoteAllocation, later.Id.Value, later.PostingId));
        (template.PayerId, template.BusinessDate).ShouldBe((Payer, TestDoubles.Today));
        template.Description.ShouldBe("Application of credit note CN/26-27/000007 to bill BIL/26-27/000002 reversed");
        later.ReversalPostingId.ShouldNotBeNull();

        Received.InOrder(() =>
        {
            _repository.GetAsync(Id, Arg.Any<CancellationToken>());
            _approvals.GetPendingAsync(ApprovalDocumentType.CreditNoteAllocation, later.Id.Value, Arg.Any<CancellationToken>());
            _bills.RestoreAsync(OtherBill, Payer, Money.Of(60m), Arg.Any<CancellationToken>());
            _ledger.PostAsync(Arg.Any<PostingTemplate>(), Arg.Any<CancellationToken>());
            _unitOfWork.CommitAsync(Arg.Any<CancellationToken>());
        });
        await _bills.Received(1).RestoreAsync(OtherBill, Payer, Money.Of(60m), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BR603_the_cashier_who_asked_can_neither_approve_nor_reject_and_nothing_is_written()
    {
        var (creditNote, _, later) = Applied();
        creditNote.RequestApplicationReversal(later.Id, "Applied to the wrong bill").IsSuccess.ShouldBeTrue();
        var request = Pending(later.Id, "Applied to the wrong bill");
        _currentUser.UserId.Returns(Cashier);

        var approved = await Approve().HandleAsync(new ApproveCreditApplicationReversalCommand(Id, later.Id, Version(), null), CancellationToken.None);
        var rejected = await Reject().HandleAsync(new RejectCreditApplicationReversalCommand(Id, later.Id, Version(), "Changed my mind"), CancellationToken.None);

        approved.Error.Code.ShouldBe(CommonErrors.ApprovalCheckerIsMakerCode);
        approved.Error.Kind.ShouldBe(ErrorKind.Forbidden);
        rejected.Error.Code.ShouldBe(CommonErrors.ApprovalCheckerIsMakerCode);
        request.Status.ShouldBe(ApprovalStatus.Pending);
        creditNote.AvailableAmount.ShouldBe(Money.Of(40m));
        await NothingWasWrittenAsync();
    }

    [Fact]
    public async Task BR605_the_rejection_puts_the_application_back_in_force_and_closes_the_request_in_one_commit()
    {
        var (creditNote, _, later) = Applied();
        creditNote.RequestApplicationReversal(later.Id, "Applied to the wrong bill").IsSuccess.ShouldBeTrue();
        var request = Pending(later.Id, "Applied to the wrong bill");

        var result = await Reject().HandleAsync(new RejectCreditApplicationReversalCommand(Id, later.Id, Version(), " The bill is right "), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Applications.Single(application => application.Id == later.Id.Value).Status.ShouldBe(CreditNoteAllocationStatus.Active);
        result.Value.AvailableAmount.ShouldBe(40.00m);
        (request.Status, request.CheckerUserId, request.CheckerComment).ShouldBe((ApprovalStatus.Rejected, Checker, "The bill is right"));
        await NothingWasPostedAsync();
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BR804_an_application_read_as_waiting_whose_request_is_no_longer_pending_is_a_conflict()
    {
        var (creditNote, _, later) = Applied();
        creditNote.RequestApplicationReversal(later.Id, "Applied to the wrong bill").IsSuccess.ShouldBeTrue();

        // Another checker decided after this credit note was read: no pending request is left.
        var approved = await Approve().HandleAsync(new ApproveCreditApplicationReversalCommand(Id, later.Id, Version(), null), CancellationToken.None);

        approved.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        later.Status.ShouldBe(CreditNoteAllocationStatus.PendingReversal);
        await NothingWasWrittenAsync();
    }

    [Fact]
    public async Task A_refusal_of_the_ledger_is_answered_as_it_came_and_nothing_is_committed()
    {
        var (creditNote, _, later) = Applied();
        creditNote.RequestApplicationReversal(later.Id, "Applied to the wrong bill").IsSuccess.ShouldBeTrue();
        Pending(later.Id, "Applied to the wrong bill");
        var refused = Error.BusinessRule("ledger_refused", "The ledger refused.");
        _ledger.PostAsync(Arg.Any<PostingTemplate>(), Arg.Any<CancellationToken>()).Returns((Result<LedgerPostingId>)refused);

        var result = await Approve().HandleAsync(new ApproveCreditApplicationReversalCommand(Id, later.Id, Version(), null), CancellationToken.None);

        result.Error.ShouldBe(refused);
        later.ReversalPostingId.ShouldBeNull();
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task A_stale_version_an_unknown_credit_note_and_an_application_that_does_not_wait_are_refused_before_anything_else()
    {
        var (_, atPosting, later) = Applied();

        var stale = await Approve().HandleAsync(new ApproveCreditApplicationReversalCommand(Id, later.Id, Convert.ToBase64String([0, 0, 0, 0, 0, 0, 7, 208]), null), CancellationToken.None);
        var unknown = await Approve().HandleAsync(new ApproveCreditApplicationReversalCommand(new CreditNoteId(Guid.NewGuid()), later.Id, Version(), null), CancellationToken.None);
        var notWaiting = await Approve().HandleAsync(new ApproveCreditApplicationReversalCommand(Id, later.Id, Version(), null), CancellationToken.None);
        var atPostingNotWaiting = await Reject().HandleAsync(new RejectCreditApplicationReversalCommand(Id, atPosting.Id, Version(), "x"), CancellationToken.None);
        var noSuchApplication = await Approve().HandleAsync(new ApproveCreditApplicationReversalCommand(Id, new CreditNoteAllocationId(Guid.NewGuid()), Version(), null), CancellationToken.None);
        var staleRequest = await Request().HandleAsync(new RequestCreditApplicationReversalCommand(Id, later.Id, Convert.ToBase64String([0, 0, 0, 0, 0, 0, 7, 208]), "x"), CancellationToken.None);

        stale.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        unknown.Error.Code.ShouldBe(BillingErrors.CreditNoteNotFoundCode);
        notWaiting.Error.Code.ShouldBe(BillingErrors.CreditApplicationNotPendingReversalCode);
        atPostingNotWaiting.Error.Code.ShouldBe(BillingErrors.CreditApplicationNotPendingReversalCode);
        noSuchApplication.Error.Code.ShouldBe(BillingErrors.CreditApplicationNotFoundCode);
        noSuchApplication.Error.Kind.ShouldBe(ErrorKind.NotFound);
        staleRequest.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        later.Status.ShouldBe(CreditNoteAllocationStatus.Active);
        await _approvals.DidNotReceiveWithAnyArgs().GetPendingAsync(default, default, default);
        _approvals.DidNotReceiveWithAnyArgs().Add(default!);
        await NothingWasWrittenAsync();
    }

    private RequestCreditApplicationReversalHandler Request() => new(
        new RequestCreditApplicationReversalCommandValidator(), _clock, _currentUser, _correlation, _repository, _approvals, _unitOfWork);

    private ApproveCreditApplicationReversalHandler Approve() => new(
        new ApproveCreditApplicationReversalCommandValidator(), _clock, _currentUser, _repository, _approvals, _bills, _ledger, _unitOfWork);

    private RejectCreditApplicationReversalHandler Reject() => new(
        new RejectCreditApplicationReversalCommandValidator(), _clock, _currentUser, _repository, _approvals, _unitOfWork);

    private static string Version() => Convert.ToBase64String(CurrentVersion);

    private async Task NothingWasPostedAsync()
    {
        await _bills.DidNotReceiveWithAnyArgs().RestoreAsync(default, default, default, default);
        await _ledger.DidNotReceiveWithAnyArgs().PostAsync(default!, default);
    }

    private async Task NothingWasWrittenAsync()
    {
        await NothingWasPostedAsync();
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    // A pending request of the cashier for the reversal of an application, with the reason as its comment.
    private ApprovalRequest Pending(CreditNoteAllocationId applicationId, string reason)
    {
        var requestedAt = new DateTimeOffset(TestDoubles.Today.ToDateTime(new TimeOnly(6, 0)), TimeSpan.Zero);
        var request = ApprovalRequest.Open(ApprovalDocumentType.CreditNoteAllocation, applicationId.Value, ApprovalAction.Reverse, Cashier, reason, requestedAt, Guid.NewGuid());
        _approvals.GetPendingAsync(ApprovalDocumentType.CreditNoteAllocation, applicationId.Value, Arg.Any<CancellationToken>()).Returns(request);

        return request;
    }

    // A posted credit note of 126.00 with 26.00 applied to its own bill at posting and 60.00 later to another bill of the
    // same payer, 40.00 left; each application with its id and its posting, as the persistence layer and the ledger left
    // them.
    private (CreditNote CreditNote, CreditNoteAllocation AtPosting, CreditNoteAllocation Later) Applied()
    {
        var creditNote = CreditNote.Create(
            OwnBill,
            Payer,
            Patient,
            TestDoubles.Today,
            TestDoubles.Today,
            "Dressing not done",
            [new CreditNoteLineDetails { Description = "Dressing", Quantity = Quantity.Of(2m), UnitPrice = UnitPrice.Of(60m), TaxRate = Percent.Of(5m) }],
            Window).Value.WithId(Id).Saved(CurrentVersion);
        creditNote.SubmitForApproval(TestDoubles.Today, Window).IsSuccess.ShouldBeTrue();
        var number = DocumentNumber.Create(DocumentType.CreditNote, FiscalYear.FromDate(TestDoubles.Today), "CN", 7);
        var postedAt = new DateTimeOffset(TestDoubles.Today.ToDateTime(new TimeOnly(4, 0)), TimeSpan.Zero);
        creditNote.PostApproved(number, postedAt, Checker, TestDoubles.Today, Window).IsSuccess.ShouldBeTrue();
        var atPosting = creditNote.ApplyToOwnBillAtPosting(Money.Of(26m), postedAt, Checker).Value.WithId(new CreditNoteAllocationId(Guid.NewGuid()));
        var later = creditNote.Apply(OtherBill, Money.Of(60m), postedAt.AddHours(1), Cashier).Value.WithId(new CreditNoteAllocationId(Guid.NewGuid()));
        creditNote.AttachApplicationPosting(atPosting, new LedgerPostingId(Guid.NewGuid()));
        creditNote.AttachApplicationPosting(later, new LedgerPostingId(Guid.NewGuid()));
        creditNote.ClearDomainEvents();
        _repository.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(creditNote);

        return (creditNote, atPosting, later);
    }
}
