using BillingRecon.Application.Adjustments;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Receivables;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Domain.Receivables;
using NSubstitute;

namespace BillingRecon.Application.Tests.Adjustments;

/// <summary>
/// Approving an adjustment that waits for approval, with the ports replaced (BR-501, BR-502, BR-603, BR-606): the
/// approval is the posting, every plain refusal comes before the number is taken, and the bill is reached only
/// through the Billing module's contract.
/// </summary>
public sealed class AdjustmentApprovalHandlerTests
{
    private static readonly byte[] CurrentVersion = [0, 0, 0, 0, 0, 0, 7, 209];
    private static readonly PayerId Payer = new(Guid.NewGuid());
    private static readonly UserId Maker = new(Guid.NewGuid());
    private static readonly UserId Checker = new(Guid.NewGuid());
    private static readonly BillId BillId = new(Guid.NewGuid());
    private static readonly AdjustmentId Id = new(Guid.NewGuid());
    private static readonly FiscalYear Fy = FiscalYear.FromDate(TestDoubles.Today);
    private static readonly BusinessDateWindow Window = new(TestDoubles.Today, 7);

    private readonly ITenantSettingsReader _settings = Substitute.For<ITenantSettingsReader>();
    private readonly IBillSettlement _bills = Substitute.For<IBillSettlement>();
    private readonly IAdjustmentRepository _repository = Substitute.For<IAdjustmentRepository>();
    private readonly IApprovalRequestRepository _approvals = Substitute.For<IApprovalRequestRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly INumberSeries _numbers = Substitute.For<INumberSeries>();
    private readonly IReceivablesLedger _ledger = Substitute.For<IReceivablesLedger>();
    private readonly IClock _clock = TestDoubles.ClockAt(TestDoubles.Today);
    private readonly IUnitOfWork _unitOfWork = TestDoubles.UnitOfWork();
    private readonly List<PostingTemplate> _posted = [];

    public AdjustmentApprovalHandlerTests()
    {
        _clock.ToIstDate(Arg.Any<DateTimeOffset>()).Returns(TestDoubles.Today);
        _settings.GetDocumentSettingsAsync(Arg.Any<CancellationToken>()).Returns(new TenantDocumentSettings(MaxBackdateDays: 7, DefaultDueDays: 15));
        _currentUser.UserId.Returns(Checker);
        _currentUser.Roles.Returns(new[] { Role.Supervisor });
        _numbers.NextAsync(DocumentType.Adjustment, Fy, Arg.Any<CancellationToken>()).Returns(DocumentNumber.Create(DocumentType.Adjustment, Fy, "ADJ", 5));
        _ledger.PostAsync(Arg.Do<PostingTemplate>(_posted.Add), Arg.Any<CancellationToken>()).Returns((Result<LedgerPostingId>)new LedgerPostingId(Guid.NewGuid()));
        BillOwes(126m);
        _bills.ApplyAsync(BillId, Payer, Arg.Any<Money>(), Arg.Any<CancellationToken>()).Returns((Result<SettledBill>)new SettledBill("BIL/26-27/000042", Money.Zero));
    }

    [Fact]
    public async Task BR606_the_approval_posts_the_adjustment_lowers_the_bill_through_billing_and_decides_the_request_in_one_commit()
    {
        var (adjustment, request) = Waiting();

        var result = await Approve().HandleAsync(new ApproveAdjustmentCommand(Id, Version(), " Agreed by the director "), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(AdjustmentStatus.Posted);
        result.Value.Number.ShouldBe($"ADJ/{Fy.ShortLabel}/000005");
        adjustment.PostedByUserId.ShouldBe(Checker, "the approval is the posting, so the checker is who posted");
        adjustment.PostingId.ShouldNotBeNull();
        (request.Status, request.CheckerUserId, request.CheckerComment).ShouldBe((ApprovalStatus.Approved, Checker, "Agreed by the director"));

        // Dr Adjustments / Cr Receivable(bill), dated the day of the adjustment (BR-502).
        var template = _posted.ShouldHaveSingleItem();
        (template.Kind, template.DocumentType, template.DocumentId, template.PayerId, template.BusinessDate)
            .ShouldBe((LedgerPostingKind.AdjustmentPosted, LedgerDocumentType.Adjustment, Id.Value, Payer, TestDoubles.Today));
        template.Description.ShouldBe($"Adjustment ADJ/{Fy.ShortLabel}/000005 posted against bill BIL/26-27/000042");

        Received.InOrder(() =>
        {
            _repository.GetAsync(Id, Arg.Any<CancellationToken>());
            _bills.FindPostedAsync(BillId, Arg.Any<CancellationToken>());
            _approvals.GetPendingAsync(ApprovalDocumentType.Adjustment, Id.Value, Arg.Any<CancellationToken>());
            _numbers.NextAsync(DocumentType.Adjustment, Fy, Arg.Any<CancellationToken>());
            _bills.ApplyAsync(BillId, Payer, Money.Of(126m), Arg.Any<CancellationToken>());
            _ledger.PostAsync(Arg.Any<PostingTemplate>(), Arg.Any<CancellationToken>());
            _unitOfWork.CommitAsync(Arg.Any<CancellationToken>());
        });
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BR501_an_adjustment_for_more_than_the_bill_is_owed_by_now_is_refused_before_a_number_is_taken()
    {
        Waiting();
        BillOwes(125.99m);

        var result = await Approve().HandleAsync(new ApproveAdjustmentCommand(Id, Version(), null), CancellationToken.None);

        result.Error.Code.ShouldBe(AdjustmentsErrors.AdjustmentExceedsBalanceDueCode);
        result.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task BR603_the_maker_cannot_approve_their_own_adjustment_and_no_number_is_taken()
    {
        var (adjustment, request) = Waiting();
        _currentUser.UserId.Returns(Maker);

        var result = await Approve().HandleAsync(new ApproveAdjustmentCommand(Id, Version(), null), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ApprovalCheckerIsMakerCode);
        result.Error.Kind.ShouldBe(ErrorKind.Forbidden);
        adjustment.Status.ShouldBe(AdjustmentStatus.PendingApproval);
        request.Status.ShouldBe(ApprovalStatus.Pending);
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task BR804_a_ledger_that_finds_the_payers_balance_going_below_zero_is_answered_as_a_conflict_and_nothing_is_committed()
    {
        Waiting();
        _ledger.PostAsync(Arg.Any<PostingTemplate>(), Arg.Any<CancellationToken>()).Returns((Result<LedgerPostingId>)ReceivablesErrors.LedgerBalanceNegative());

        var ledgerRefused = await Approve().HandleAsync(new ApproveAdjustmentCommand(Id, Version(), null), CancellationToken.None);

        ledgerRefused.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode, "the bill accepted the amount as it was read, so somebody else committed in between");
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task A_stale_version_an_unknown_adjustment_one_that_does_not_wait_and_a_scope_without_a_user_are_refused_before_anything_else()
    {
        Waiting();

        var stale = await Approve().HandleAsync(new ApproveAdjustmentCommand(Id, Convert.ToBase64String([0, 0, 0, 0, 0, 0, 7, 208]), null), CancellationToken.None);
        var unknown = await Approve().HandleAsync(new ApproveAdjustmentCommand(new AdjustmentId(Guid.NewGuid()), Version(), null), CancellationToken.None);
        _currentUser.UserId.Returns((UserId?)null);
        var nobody = await Approve().HandleAsync(new ApproveAdjustmentCommand(Id, Version(), null), CancellationToken.None);
        var draft = Draft();
        _repository.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(draft);
        var notWaiting = await Approve().HandleAsync(new ApproveAdjustmentCommand(Id, Version(), null), CancellationToken.None);

        stale.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        unknown.Error.Code.ShouldBe(AdjustmentsErrors.AdjustmentNotFoundCode);
        nobody.Error.Code.ShouldBe(CommonErrors.ApprovalNeedsUserCode);
        notWaiting.Error.Code.ShouldBe(AdjustmentsErrors.AdjustmentNotPendingApprovalCode);
        await NothingWasTakenOrWrittenAsync();
    }

    private ApproveAdjustmentHandler Approve() => new(
        new ApproveAdjustmentCommandValidator(), _clock, _currentUser, _bills, _repository, _approvals, _settings, _numbers, _ledger, _unitOfWork);

    private static string Version() => Convert.ToBase64String(CurrentVersion);

    private void BillOwes(decimal balanceDue)
        => _bills.FindPostedAsync(BillId, Arg.Any<CancellationToken>())
            .Returns((Result<PostedBill>)new PostedBill(Payer, TestDoubles.Today, "BIL/26-27/000042", Money.Of(balanceDue)));

    private async Task NothingWasTakenOrWrittenAsync()
    {
        await _numbers.DidNotReceiveWithAnyArgs().NextAsync(default, default, default);
        await _bills.DidNotReceiveWithAnyArgs().ApplyAsync(default, default, default, default);
        await _ledger.DidNotReceiveWithAnyArgs().PostAsync(default!, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    // A write-off of the whole bill, 126.00.
    private static Adjustment Draft() => Adjustment.Create(
        BillId, Payer, TestDoubles.Today, TestDoubles.Today, AdjustmentKind.WriteOff, Money.Of(126m), "Patient cannot pay", Window).Value.WithId(Id).Saved(CurrentVersion);

    // The adjustment of these tests, waiting, with its pending request of the maker.
    private (Adjustment Adjustment, ApprovalRequest Request) Waiting()
    {
        var adjustment = Draft();
        adjustment.SubmitForApproval(TestDoubles.Today, Window).IsSuccess.ShouldBeTrue();
        var requestedAt = new DateTimeOffset(TestDoubles.Today.ToDateTime(new TimeOnly(6, 0)), TimeSpan.Zero);
        var request = ApprovalRequest.Open(ApprovalDocumentType.Adjustment, Id.Value, ApprovalAction.Post, Maker, makerComment: null, requestedAt, Guid.NewGuid());
        _repository.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(adjustment);
        _approvals.GetPendingAsync(ApprovalDocumentType.Adjustment, Id.Value, Arg.Any<CancellationToken>()).Returns(request);

        return (adjustment, request);
    }
}
