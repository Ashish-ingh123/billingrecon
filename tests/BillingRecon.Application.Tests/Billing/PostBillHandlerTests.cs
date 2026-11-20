using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Receivables;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Domain.Receivables;
using NSubstitute;

namespace BillingRecon.Application.Tests.Billing;

/// <summary>
/// Posting a bill with its ports replaced (US-21, US-25; BR-206, BR-207, BR-801, BR-804): every refusal comes before
/// the number is taken, the number comes before the ledger, the ledger before the commit, and a failure after the
/// number leaves the commit undone, so that the transaction takes the number back.
/// </summary>
public sealed class PostBillHandlerTests
{
    private static readonly byte[] CurrentVersion = [0, 0, 0, 0, 0, 0, 7, 209];
    private static readonly byte[] OlderVersion = [0, 0, 0, 0, 0, 0, 7, 208];
    private static readonly PatientId Patient = new(Guid.NewGuid());
    private static readonly PayerId Payer = new(Guid.NewGuid());
    private static readonly UserId Clerk = new(Guid.NewGuid());
    private static readonly BillId Id = new(Guid.NewGuid());
    private static readonly Guid Correlation = Guid.NewGuid();
    private static readonly FiscalYear Fy = FiscalYear.FromDate(TestDoubles.Today);

    private readonly IBillableParties _parties = Substitute.For<IBillableParties>();
    private readonly ITenantSettingsReader _settings = Substitute.For<ITenantSettingsReader>();
    private readonly IBillRepository _repository = Substitute.For<IBillRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly INumberSeries _numbers = Substitute.For<INumberSeries>();
    private readonly IReceivablesLedger _ledger = Substitute.For<IReceivablesLedger>();
    private readonly IApprovalRequestRepository _approvals = Substitute.For<IApprovalRequestRepository>();
    private readonly ICorrelationContext _correlation = Substitute.For<ICorrelationContext>();
    private readonly IClock _clock = TestDoubles.ClockAt(TestDoubles.Today);
    private IUnitOfWork _unitOfWork = TestDoubles.UnitOfWork();

    public PostBillHandlerTests()
    {
        _clock.ToIstDate(Arg.Any<DateTimeOffset>()).Returns(TestDoubles.Today);
        _parties.EnsureCanOweAsync(Payer, Arg.Any<CancellationToken>()).Returns(Result.Success());
        _settings.GetDocumentSettingsAsync(Arg.Any<CancellationToken>()).Returns(new TenantDocumentSettings(MaxBackdateDays: 7, DefaultDueDays: 15));
        _currentUser.UserId.Returns(Clerk);
        _correlation.CorrelationId.Returns(Correlation);
        _numbers.NextAsync(DocumentType.Bill, Fy, Arg.Any<CancellationToken>()).Returns(DocumentNumber.Create(DocumentType.Bill, Fy, "BIL", 42));
        _ledger.PostAsync(Arg.Any<PostingTemplate>(), Arg.Any<CancellationToken>()).Returns((Result<LedgerPostingId>)new LedgerPostingId(Guid.NewGuid()));
    }

    [Fact]
    public async Task US21_a_draft_is_posted_under_the_next_number_entered_in_the_ledger_and_committed_once()
    {
        var bill = Saved(Draft(FreeText()));
        PostingTemplate? entered = null;
        await _ledger.PostAsync(Arg.Do<PostingTemplate>(template => entered = template), Arg.Any<CancellationToken>());

        var result = await Handler().HandleAsync(new PostBillCommand(Id, Version(CurrentVersion)), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(BillStatus.Posted);
        result.Value.Number.ShouldBe($"BIL/{Fy.ShortLabel}/000042");
        result.Value.FiscalYear.ShouldBe(Fy.Label);
        result.Value.PostedAtUtc.ShouldBe(_clock.UtcNow);
        result.Value.BalanceDue.ShouldBe(126.00m);
        bill.PostedByUserId.ShouldBe(Clerk);

        // Dr Receivable(bill) / Cr Revenue for the total, on the bill date, for the payer of the bill (BR-206).
        var template = entered.ShouldNotBeNull();
        template.Kind.ShouldBe(LedgerPostingKind.BillPosted);
        template.DocumentType.ShouldBe(LedgerDocumentType.Bill);
        template.DocumentId.ShouldBe(bill.Id.Value);
        template.PayerId.ShouldBe(Payer);
        template.BusinessDate.ShouldBe(TestDoubles.Today);
        template.Description.ShouldBe($"Bill BIL/{Fy.ShortLabel}/000042 posted");
        template.Lines.ShouldBe(
        [
            LedgerLine.Dr(LedgerAccount.Receivable, Money.Of(126m), new LedgerSubject(LedgerSubjectType.Bill, bill.Id.Value)),
            LedgerLine.Cr(LedgerAccount.Revenue, Money.Of(126m)),
        ]);

        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BR801_the_number_is_taken_after_every_check_and_before_the_ledger_and_the_commit()
    {
        Saved(Draft(FreeText()));

        await Handler().HandleAsync(new PostBillCommand(Id, Version(CurrentVersion)), CancellationToken.None);

        Received.InOrder(() =>
        {
            _repository.GetAsync(Id, Arg.Any<CancellationToken>());
            _settings.GetDocumentSettingsAsync(Arg.Any<CancellationToken>());
            _parties.EnsureCanOweAsync(Payer, Arg.Any<CancellationToken>());
            _numbers.NextAsync(DocumentType.Bill, Fy, Arg.Any<CancellationToken>());
            _ledger.PostAsync(Arg.Any<PostingTemplate>(), Arg.Any<CancellationToken>());
            _unitOfWork.CommitAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task The_clock_is_read_once_for_the_instant_and_for_today()
    {
        Saved(Draft(FreeText()));

        await Handler().HandleAsync(new PostBillCommand(Id, Version(CurrentVersion)), CancellationToken.None);

        _ = _clock.Received(1).UtcNow;
        _ = _clock.DidNotReceive().TodayIst;
    }

    [Fact]
    public async Task BR206_a_bill_that_comes_to_nothing_is_settled_with_its_number_and_without_a_ledger_posting()
    {
        Saved(Draft(FreeText() with { DiscountAmount = Money.Of(120m), TaxRate = Percent.Of(0m) }));

        var result = await Handler().HandleAsync(new PostBillCommand(Id, Version(CurrentVersion)), CancellationToken.None);

        result.Value.Status.ShouldBe(BillStatus.Settled);
        result.Value.Total.ShouldBe(0m);
        result.Value.Number.ShouldNotBeNull();
        await _ledger.DidNotReceiveWithAnyArgs().PostAsync(default!, default);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_bill_that_does_not_exist_is_not_found_and_nothing_is_taken()
    {
        var result = await Handler().HandleAsync(new PostBillCommand(Id, Version(CurrentVersion)), CancellationToken.None);

        result.Error.Code.ShouldBe(BillingErrors.BillNotFoundCode);
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task BR804_a_stale_version_is_refused_before_anything_else()
    {
        Saved(Draft(FreeText()));

        var result = await Handler().HandleAsync(new PostBillCommand(Id, Version(OlderVersion)), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        await _settings.DidNotReceiveWithAnyArgs().GetDocumentSettingsAsync(default);
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task A_draft_without_lines_is_refused_and_no_number_is_taken()
    {
        Saved(Draft());

        var result = await Handler().HandleAsync(new PostBillCommand(Id, Version(CurrentVersion)), CancellationToken.None);

        result.Error.Code.ShouldBe(BillingErrors.BillHasNoLinesCode);
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task BR205_a_draft_that_has_grown_older_than_the_tenant_allows_is_refused_and_no_number_is_taken()
    {
        Saved(Draft(FreeText()));
        _clock.ToIstDate(Arg.Any<DateTimeOffset>()).Returns(TestDoubles.Today.AddDays(8));

        var result = await Handler().HandleAsync(new PostBillCommand(Id, Version(CurrentVersion)), CancellationToken.None);

        result.Error.Code.ShouldBe(BillingErrors.BillDateTooOldCode);
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task BR105_a_bill_of_an_inactive_payer_is_refused_and_no_number_is_taken()
    {
        Saved(Draft(FreeText()));
        _parties.EnsureCanOweAsync(Payer, Arg.Any<CancellationToken>()).Returns(PayersErrors.PayerInactive());

        var result = await Handler().HandleAsync(new PostBillCommand(Id, Version(CurrentVersion)), CancellationToken.None);

        result.Error.Code.ShouldBe(PayersErrors.PayerInactiveCode);
        await _parties.DidNotReceiveWithAnyArgs().EnsureCanBeBilledAsync(default, default, default);
        await NothingWasTakenOrWrittenAsync();
    }

    [Theory]
    [InlineData(126.00, false)]
    [InlineData(125.99, false)]
    [InlineData(126.01, true)]
    public async Task BR207_a_bill_at_or_above_the_approval_threshold_is_handed_to_a_second_person_and_not_posted(decimal threshold, bool posted)
    {
        // Until Prompt 3.6 such a bill was refused with bill_requires_approval; maker-checker replaces the refusal.
        var bill = Saved(Draft(FreeText()));
        ApprovalRequest? opened = null;
        _approvals.Add(Arg.Do<ApprovalRequest>(request => opened = request));
        _settings.GetDocumentSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new TenantDocumentSettings(MaxBackdateDays: 7, DefaultDueDays: 15, PostingApprovalThreshold: Money.Of(threshold)));

        var result = await Handler().HandleAsync(new PostBillCommand(Id, Version(CurrentVersion)), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        if (posted)
        {
            result.Value.Status.ShouldBe(BillStatus.Posted);
            opened.ShouldBeNull();

            return;
        }

        result.Value.Status.ShouldBe(BillStatus.PendingApproval);
        result.Value.Number.ShouldBeNull("the number is assigned when the checker approves (BR-207)");
        result.Value.PostedAtUtc.ShouldBeNull();
        var request = opened.ShouldNotBeNull();
        request.DocumentType.ShouldBe(ApprovalDocumentType.Bill);
        request.DocumentId.ShouldBe(bill.Id.Value);
        request.Action.ShouldBe(ApprovalAction.Post);
        request.MakerUserId.ShouldBe(Clerk);
        request.Status.ShouldBe(ApprovalStatus.Pending);
        request.CorrelationId.ShouldBe(Correlation);
        await _numbers.DidNotReceiveWithAnyArgs().NextAsync(default, default, default);
        await _ledger.DidNotReceiveWithAnyArgs().PostAsync(default!, default);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BR603_a_bill_that_needs_approval_cannot_be_submitted_without_a_user()
    {
        Saved(Draft(FreeText()));
        _currentUser.UserId.Returns((UserId?)null);
        _settings.GetDocumentSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new TenantDocumentSettings(MaxBackdateDays: 7, DefaultDueDays: 15, PostingApprovalThreshold: Money.Of(1m)));

        var result = await Handler().HandleAsync(new PostBillCommand(Id, Version(CurrentVersion)), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ApprovalNeedsUserCode);
        result.Error.Kind.ShouldBe(ErrorKind.Forbidden);
        _approvals.DidNotReceiveWithAnyArgs().Add(default!);
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task US25_a_ledger_that_refuses_the_posting_leaves_the_commit_undone()
    {
        Saved(Draft(FreeText()));
        _ledger.PostAsync(Arg.Any<PostingTemplate>(), Arg.Any<CancellationToken>()).Returns((Result<LedgerPostingId>)ReceivablesErrors.LedgerBalanceNegative());

        var result = await Handler().HandleAsync(new PostBillCommand(Id, Version(CurrentVersion)), CancellationToken.None);

        // The number was taken inside the transaction; without a commit the transaction ends in a rollback, and the
        // number goes back with it (BR-801).
        result.Error.Code.ShouldBe(ReceivablesErrors.LedgerBalanceNegativeCode);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task US25_a_number_series_or_a_ledger_that_fails_with_a_fault_lets_the_fault_through_without_a_commit()
    {
        Saved(Draft(FreeText()));
        _ledger.PostAsync(Arg.Any<PostingTemplate>(), Arg.Any<CancellationToken>()).Returns<Task<Result<LedgerPostingId>>>(_ => throw new InvalidOperationException("The ledger failed."));

        await Should.ThrowAsync<InvalidOperationException>(() => Handler().HandleAsync(new PostBillCommand(Id, Version(CurrentVersion)), CancellationToken.None));

        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task BR804_a_commit_that_loses_to_another_posting_of_the_bill_answers_the_conflict()
    {
        Saved(Draft(FreeText()));
        _unitOfWork = TestDoubles.UnitOfWork(CommonErrors.ConcurrencyConflict());

        var result = await Handler().HandleAsync(new PostBillCommand(Id, Version(CurrentVersion)), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
    }

    private async Task NothingWasTakenOrWrittenAsync()
    {
        await _numbers.DidNotReceiveWithAnyArgs().NextAsync(default, default, default);
        await _ledger.DidNotReceiveWithAnyArgs().PostAsync(default!, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    private PostBillHandler Handler() => new(_clock, _currentUser, _correlation, _repository, _approvals, _settings, _parties, _numbers, _ledger, _unitOfWork);

    private Bill Saved(Bill bill)
    {
        bill.Saved(CurrentVersion).WithId(Id);
        _repository.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(bill);

        return bill;
    }

    private static string Version(byte[] version) => Convert.ToBase64String(version);

    private static Bill Draft(params BillLineDetails[] lines) => Bill.Create(
        Patient,
        Payer,
        new BillHeader { BillDate = TestDoubles.Today, DueDate = TestDoubles.Today.AddDays(15) },
        lines,
        new BusinessDateWindow(TestDoubles.Today, 7)).Value;

    // 2 x 60.00 at 5%: 120.00 + 6.00.
    private static BillLineDetails FreeText() => new()
    {
        Description = "Dressing",
        Quantity = Quantity.Of(2m),
        UnitPrice = UnitPrice.Of(60m),
        TaxRate = Percent.Of(5m),
    };
}
