using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Receivables;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using NSubstitute;

namespace BillingRecon.Application.Tests.Billing;

/// <summary>
/// Approving and rejecting a bill that waits for approval, with the ports replaced (US-22; BR-603, BR-605, BR-606):
/// the approval is the posting, every refusal comes before the number is taken, and the request and the bill are
/// saved by one commit.
/// </summary>
public sealed class BillApprovalHandlerTests
{
    private static readonly byte[] CurrentVersion = [0, 0, 0, 0, 0, 0, 7, 209];
    private static readonly byte[] OlderVersion = [0, 0, 0, 0, 0, 0, 7, 208];
    private static readonly PatientId Patient = new(Guid.NewGuid());
    private static readonly PayerId Payer = new(Guid.NewGuid());
    private static readonly UserId Maker = new(Guid.NewGuid());
    private static readonly UserId Checker = new(Guid.NewGuid());
    private static readonly BillId Id = new(Guid.NewGuid());
    private static readonly FiscalYear Fy = FiscalYear.FromDate(TestDoubles.Today);
    private static readonly BusinessDateWindow Window = new(TestDoubles.Today, 7);

    private readonly IBillableParties _parties = Substitute.For<IBillableParties>();
    private readonly ITenantSettingsReader _settings = Substitute.For<ITenantSettingsReader>();
    private readonly IBillRepository _repository = Substitute.For<IBillRepository>();
    private readonly IApprovalRequestRepository _approvals = Substitute.For<IApprovalRequestRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly INumberSeries _numbers = Substitute.For<INumberSeries>();
    private readonly IReceivablesLedger _ledger = Substitute.For<IReceivablesLedger>();
    private readonly IClock _clock = TestDoubles.ClockAt(TestDoubles.Today);
    private readonly IUnitOfWork _unitOfWork = TestDoubles.UnitOfWork();

    public BillApprovalHandlerTests()
    {
        _clock.ToIstDate(Arg.Any<DateTimeOffset>()).Returns(TestDoubles.Today);
        _parties.EnsureCanOweAsync(Payer, Arg.Any<CancellationToken>()).Returns(Result.Success());
        _settings.GetDocumentSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new TenantDocumentSettings(MaxBackdateDays: 7, DefaultDueDays: 15, PostingApprovalThreshold: Money.Of(100m)));
        _currentUser.UserId.Returns(Checker);
        _numbers.NextAsync(DocumentType.Bill, Fy, Arg.Any<CancellationToken>()).Returns(DocumentNumber.Create(DocumentType.Bill, Fy, "BIL", 42));
        _ledger.PostAsync(Arg.Any<PostingTemplate>(), Arg.Any<CancellationToken>()).Returns((Result<LedgerPostingId>)new LedgerPostingId(Guid.NewGuid()));
    }

    [Fact]
    public async Task BR606_the_checkers_approval_posts_the_bill_and_decides_the_request_in_one_commit()
    {
        var (bill, request) = Waiting();

        var result = await Approve().HandleAsync(new ApproveBillPostingCommand(Id, Version(CurrentVersion), " Checked against the estimate "), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(BillStatus.Posted);
        result.Value.Number.ShouldBe($"BIL/{Fy.ShortLabel}/000042");
        bill.PostedByUserId.ShouldBe(Checker, "the approval is the posting, so the checker is who posted");
        request.Status.ShouldBe(ApprovalStatus.Approved);
        request.CheckerUserId.ShouldBe(Checker);
        request.CheckerComment.ShouldBe("Checked against the estimate");
        request.DecidedAtUtc.ShouldBe(_clock.UtcNow);

        Received.InOrder(() =>
        {
            _repository.GetAsync(Id, Arg.Any<CancellationToken>());
            _approvals.GetPendingAsync(ApprovalDocumentType.Bill, bill.Id.Value, Arg.Any<CancellationToken>());
            _parties.EnsureCanOweAsync(Payer, Arg.Any<CancellationToken>());
            _numbers.NextAsync(DocumentType.Bill, Fy, Arg.Any<CancellationToken>());
            _ledger.PostAsync(Arg.Any<PostingTemplate>(), Arg.Any<CancellationToken>());
            _unitOfWork.CommitAsync(Arg.Any<CancellationToken>());
        });
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BR603_the_maker_cannot_approve_their_own_bill_and_no_number_is_taken()
    {
        var (bill, request) = Waiting();
        _currentUser.UserId.Returns(Maker);

        var result = await Approve().HandleAsync(new ApproveBillPostingCommand(Id, Version(CurrentVersion), null), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ApprovalCheckerIsMakerCode);
        result.Error.Kind.ShouldBe(ErrorKind.Forbidden);
        bill.Status.ShouldBe(BillStatus.PendingApproval);
        request.Status.ShouldBe(ApprovalStatus.Pending);
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task BR603_an_approval_needs_a_user()
    {
        Waiting();
        _currentUser.UserId.Returns((UserId?)null);

        var result = await Approve().HandleAsync(new ApproveBillPostingCommand(Id, Version(CurrentVersion), null), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ApprovalNeedsUserCode);
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task A_draft_that_nobody_submitted_cannot_be_approved()
    {
        var draft = NewDraft().Saved(CurrentVersion);
        _repository.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(draft);

        var result = await Approve().HandleAsync(new ApproveBillPostingCommand(Id, Version(CurrentVersion), null), CancellationToken.None);

        result.Error.Code.ShouldBe(BillingErrors.BillNotPendingApprovalCode);
        await _approvals.DidNotReceiveWithAnyArgs().GetPendingAsync(default, default, default);
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task BR804_a_stale_version_and_an_unknown_bill_are_refused_before_anything_else()
    {
        Waiting();

        var stale = await Approve().HandleAsync(new ApproveBillPostingCommand(Id, Version(OlderVersion), null), CancellationToken.None);
        var unknown = await Approve().HandleAsync(new ApproveBillPostingCommand(new BillId(Guid.NewGuid()), Version(CurrentVersion), null), CancellationToken.None);

        stale.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        unknown.Error.Code.ShouldBe(BillingErrors.BillNotFoundCode);
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task BR105_BR205_the_checks_of_a_posting_are_made_again_when_the_checker_approves()
    {
        Waiting();
        _parties.EnsureCanOweAsync(Payer, Arg.Any<CancellationToken>()).Returns(PayersErrors.PayerInactive());

        var payerInactive = await Approve().HandleAsync(new ApproveBillPostingCommand(Id, Version(CurrentVersion), null), CancellationToken.None);

        payerInactive.Error.Code.ShouldBe(PayersErrors.PayerInactiveCode);
        await NothingWasTakenOrWrittenAsync();

        // The bill waited eight days: its date is older than the hospital allows a bill to be back-dated.
        Waiting();
        _clock.ToIstDate(Arg.Any<DateTimeOffset>()).Returns(TestDoubles.Today.AddDays(8));

        var tooOld = await Approve().HandleAsync(new ApproveBillPostingCommand(Id, Version(CurrentVersion), null), CancellationToken.None);

        tooOld.Error.Code.ShouldBe(BillingErrors.BillDateTooOldCode);
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task A_comment_longer_than_its_column_is_refused_before_the_bill_is_read()
    {
        var result = await Approve().HandleAsync(new ApproveBillPostingCommand(Id, Version(CurrentVersion), new string('c', ApprovalRequest.CommentMaxLength + 1)), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        result.Error.FieldErrors.Keys.ShouldBe(["comment"]);
        await _repository.DidNotReceiveWithAnyArgs().GetAsync(default, default);
    }

    [Fact]
    public async Task BR804_a_bill_read_as_waiting_whose_request_is_no_longer_pending_is_a_conflict_and_nothing_is_taken_or_written()
    {
        // Changed expectation (Prompt 4.5, ADR 0027): this used to be a fault, InvalidOperationException, and so a 500.
        // It is what an ordinary race looks like: another checker's decision was committed after this request read the
        // bill and before it looked for the approval request. The bill it read is stale, which is a conflict.
        var bill = NewDraft();
        bill.SubmitForApproval(Window);
        _repository.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(bill.Saved(CurrentVersion));

        var result = await Approve().HandleAsync(new ApproveBillPostingCommand(Id, Version(CurrentVersion), null), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task BR605_a_rejection_makes_the_bill_a_draft_again_and_closes_the_request_with_the_comment()
    {
        var (bill, request) = Waiting();

        var result = await Reject().HandleAsync(new RejectBillPostingCommand(Id, Version(CurrentVersion), " Wrong consultant fee "), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(BillStatus.Draft);
        result.Value.Number.ShouldBeNull();
        bill.Status.ShouldBe(BillStatus.Draft);
        request.Status.ShouldBe(ApprovalStatus.Rejected);
        request.CheckerUserId.ShouldBe(Checker);
        request.CheckerComment.ShouldBe("Wrong consultant fee");
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BR605_a_rejection_says_why(string comment)
    {
        Waiting();

        var result = await Reject().HandleAsync(new RejectBillPostingCommand(Id, Version(CurrentVersion), comment), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        result.Error.FieldErrors.Keys.ShouldBe(["comment"]);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task BR603_the_maker_cannot_reject_their_own_bill_either()
    {
        var (_, request) = Waiting();
        _currentUser.UserId.Returns(Maker);

        var result = await Reject().HandleAsync(new RejectBillPostingCommand(Id, Version(CurrentVersion), "Changed my mind"), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ApprovalCheckerIsMakerCode);
        request.Status.ShouldBe(ApprovalStatus.Pending);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task A_draft_cannot_be_rejected_and_a_stale_version_is_refused()
    {
        _repository.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(NewDraft().Saved(CurrentVersion));

        var notWaiting = await Reject().HandleAsync(new RejectBillPostingCommand(Id, Version(CurrentVersion), "No"), CancellationToken.None);
        var stale = await Reject().HandleAsync(new RejectBillPostingCommand(Id, Version(OlderVersion), "No"), CancellationToken.None);

        notWaiting.Error.Code.ShouldBe(BillingErrors.BillNotPendingApprovalCode);
        stale.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    private async Task NothingWasTakenOrWrittenAsync()
    {
        await _numbers.DidNotReceiveWithAnyArgs().NextAsync(default, default, default);
        await _ledger.DidNotReceiveWithAnyArgs().PostAsync(default!, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    private ApproveBillPostingHandler Approve()
        => new(new ApproveBillPostingCommandValidator(), _clock, _currentUser, _repository, _approvals, _settings, _parties, _numbers, _ledger, _unitOfWork);

    private RejectBillPostingHandler Reject()
        => new(new RejectBillPostingCommandValidator(), _clock, _currentUser, _repository, _approvals, _unitOfWork);

    // A bill the maker submitted, with its pending request, as the repositories would return them.
    private (Bill Bill, ApprovalRequest Request) Waiting()
    {
        var bill = NewDraft();
        bill.SubmitForApproval(Window).IsSuccess.ShouldBeTrue();
        bill.Saved(CurrentVersion);
        var request = ApprovalRequest.Open(ApprovalDocumentType.Bill, Guid.NewGuid(), ApprovalAction.Post, Maker, null, _clock.UtcNow, Guid.NewGuid());

        _repository.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(bill);
        _approvals.GetPendingAsync(ApprovalDocumentType.Bill, bill.Id.Value, Arg.Any<CancellationToken>()).Returns(request);

        return (bill, request);
    }

    private static string Version(byte[] version) => Convert.ToBase64String(version);

    // 2 x 60.00 at 5%: 126.00, above the threshold of 100.00.
    private static Bill NewDraft() => Bill.Create(
        Patient,
        Payer,
        new BillHeader { BillDate = TestDoubles.Today, DueDate = TestDoubles.Today.AddDays(15) },
        [new BillLineDetails { Description = "Dressing", Quantity = Quantity.Of(2m), UnitPrice = UnitPrice.Of(60m), TaxRate = Percent.Of(5m) }],
        Window).Value;
}
