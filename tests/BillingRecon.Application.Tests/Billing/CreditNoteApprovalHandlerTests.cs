using BillingRecon.Application.Adjustments;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Receivables;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Domain.Receivables;
using NSubstitute;

namespace BillingRecon.Application.Tests.Billing;

/// <summary>
/// Approving a credit note that waits for approval, with the ports replaced (US-24; BR-210, BR-212, BR-603, BR-606):
/// the approval is the posting, every plain refusal comes before the number is taken, the caps are measured again
/// after the ledger has locked the payer, and the credit goes to the bill as far as the bill is owed.
/// </summary>
public sealed class CreditNoteApprovalHandlerTests
{
    private static readonly byte[] CurrentVersion = [0, 0, 0, 0, 0, 0, 7, 209];
    private static readonly PatientId Patient = new(Guid.NewGuid());
    private static readonly PayerId Payer = new(Guid.NewGuid());
    private static readonly UserId Maker = new(Guid.NewGuid());
    private static readonly UserId Checker = new(Guid.NewGuid());
    private static readonly BillId BillId = new(Guid.NewGuid());
    private static readonly CreditNoteId Id = new(Guid.NewGuid());
    private static readonly FiscalYear Fy = FiscalYear.FromDate(TestDoubles.Today);
    private static readonly BusinessDateWindow Window = new(TestDoubles.Today, 7);

    private readonly ITenantSettingsReader _settings = Substitute.For<ITenantSettingsReader>();
    private readonly IBillRepository _bills = Substitute.For<IBillRepository>();
    private readonly ICreditNoteRepository _repository = Substitute.For<ICreditNoteRepository>();

    // What the bill's posted adjustments took off it; nothing, unless a test says otherwise.
    private readonly IBillAdjustments _adjustments = Substitute.For<IBillAdjustments>();
    private readonly IApprovalRequestRepository _approvals = Substitute.For<IApprovalRequestRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly INumberSeries _numbers = Substitute.For<INumberSeries>();
    private readonly IReceivablesLedger _ledger = Substitute.For<IReceivablesLedger>();
    private readonly IClock _clock = TestDoubles.ClockAt(TestDoubles.Today);
    private readonly IUnitOfWork _unitOfWork = TestDoubles.UnitOfWork();
    private readonly List<PostingTemplate> _posted = [];

    public CreditNoteApprovalHandlerTests()
    {
        _clock.ToIstDate(Arg.Any<DateTimeOffset>()).Returns(TestDoubles.Today);
        _settings.GetDocumentSettingsAsync(Arg.Any<CancellationToken>()).Returns(new TenantDocumentSettings(MaxBackdateDays: 7, DefaultDueDays: 15));
        _currentUser.UserId.Returns(Checker);
        _currentUser.Roles.Returns(new[] { Role.Supervisor });
        _numbers.NextAsync(DocumentType.CreditNote, Fy, Arg.Any<CancellationToken>()).Returns(DocumentNumber.Create(DocumentType.CreditNote, Fy, "CN", 9));
        _ledger.PostAsync(Arg.Do<PostingTemplate>(_posted.Add), Arg.Any<CancellationToken>()).Returns((Result<LedgerPostingId>)new LedgerPostingId(Guid.NewGuid()));
        _repository.ListPostedForBillAsync(BillId, Arg.Any<CancellationToken>()).Returns([]);
    }

    [Fact]
    public async Task BR606_the_approval_posts_the_credit_note_applies_it_to_its_bill_and_decides_the_request_in_one_commit()
    {
        var bill = PostedBill();
        var (creditNote, request) = Waiting();

        var result = await Approve().HandleAsync(new ApproveCreditNoteCommand(Id, Version(), " Dressing was not done "), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(CreditNoteStatus.Posted);
        result.Value.Number.ShouldBe($"CN/{Fy.ShortLabel}/000009");
        result.Value.AvailableAmount.ShouldBe(0.00m);
        result.Value.Applications.ShouldHaveSingleItem().Amount.ShouldBe(126.00m);
        creditNote.PostedByUserId.ShouldBe(Checker, "the approval is the posting, so the checker is who posted");
        (bill.Status, bill.BalanceDue).ShouldBe((BillStatus.Settled, Money.Zero));
        (request.Status, request.CheckerUserId, request.CheckerComment).ShouldBe((ApprovalStatus.Approved, Checker, "Dressing was not done"));

        // Dr Revenue / Cr CreditAvailable first, so that the credit exists before it is applied.
        _posted.Select(template => (template.Kind, template.DocumentType)).ShouldBe(
        [
            (LedgerPostingKind.CreditNotePosted, LedgerDocumentType.CreditNote),
            (LedgerPostingKind.CreditApplied, LedgerDocumentType.CreditNoteAllocation),
        ]);
        _posted[0].BusinessDate.ShouldBe(creditNote.CreditNoteDate);
        _posted[1].Description.ShouldBe($"Credit note CN/{Fy.ShortLabel}/000009 applied to bill {bill.Number}");
        creditNote.Allocations.Single().PostingId.Value.ShouldNotBe(Guid.Empty);

        Received.InOrder(() =>
        {
            _repository.GetAsync(Id, Arg.Any<CancellationToken>());
            _approvals.GetPendingAsync(ApprovalDocumentType.CreditNote, Id.Value, Arg.Any<CancellationToken>());
            _repository.ListPostedForBillAsync(BillId, Arg.Any<CancellationToken>());
            _numbers.NextAsync(DocumentType.CreditNote, Fy, Arg.Any<CancellationToken>());
            _ledger.PostAsync(Arg.Any<PostingTemplate>(), Arg.Any<CancellationToken>());
            _repository.ListPostedForBillAsync(BillId, Arg.Any<CancellationToken>());
            _ledger.PostAsync(Arg.Any<PostingTemplate>(), Arg.Any<CancellationToken>());
            _unitOfWork.CommitAsync(Arg.Any<CancellationToken>());
        });
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BR212_only_what_the_bill_is_owed_is_applied_and_the_rest_stays_available()
    {
        var bill = PostedBill();
        bill.ApplyCredit(Money.Of(100m)).IsSuccess.ShouldBeTrue();
        Waiting();

        var result = await Approve().HandleAsync(new ApproveCreditNoteCommand(Id, Version(), null), CancellationToken.None);

        result.Value.AvailableAmount.ShouldBe(100.00m);
        result.Value.Applications.ShouldHaveSingleItem().Amount.ShouldBe(26.00m);
        (bill.Status, bill.BalanceDue).ShouldBe((BillStatus.Settled, Money.Zero));
    }

    [Fact]
    public async Task BR212_a_bill_that_is_owed_nothing_gets_nothing_applied_and_the_whole_credit_stays_available()
    {
        var bill = PostedBill();
        bill.ApplyCredit(Money.Of(126m)).IsSuccess.ShouldBeTrue();
        bill.ClearDomainEvents();
        Waiting();

        var result = await Approve().HandleAsync(new ApproveCreditNoteCommand(Id, Version(), null), CancellationToken.None);

        result.Value.Status.ShouldBe(CreditNoteStatus.Posted);
        result.Value.AvailableAmount.ShouldBe(126.00m);
        result.Value.Applications.ShouldBeEmpty();
        _posted.ShouldHaveSingleItem().Kind.ShouldBe(LedgerPostingKind.CreditNotePosted);
        bill.DomainEvents.ShouldBeEmpty("the bill was not touched");
        _repository.DidNotReceiveWithAnyArgs().Add((CreditNoteAllocation)default!);
    }

    [Fact]
    public async Task BR210_a_credit_note_that_no_longer_fits_the_bill_is_refused_before_a_number_is_taken()
    {
        PostedBill();
        Waiting();
        var other = PostedElsewhere();
        _repository.ListPostedForBillAsync(BillId, Arg.Any<CancellationToken>()).Returns([other]);

        var result = await Approve().HandleAsync(new ApproveCreditNoteCommand(Id, Version(), null), CancellationToken.None);

        result.Error.Code.ShouldBe(BillingErrors.CreditNoteExceedsBillTotalCode);
        result.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task BR210_a_credit_note_that_stopped_fitting_while_it_waited_for_the_lock_is_a_conflict_and_is_not_committed()
    {
        var bill = PostedBill();
        Waiting();

        // Nothing posted when the caps are first read; another credit note of the bill is, once the ledger has the lock.
        IReadOnlyList<CreditNote> nothingYet = [];
        IReadOnlyList<CreditNote> postedSince = [PostedElsewhere()];
        var reads = 0;
        _repository.ListPostedForBillAsync(BillId, Arg.Any<CancellationToken>()).Returns(_ => reads++ == 0 ? nothingYet : postedSince);

        var result = await Approve().HandleAsync(new ApproveCreditNoteCommand(Id, Version(), null), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        _posted.ShouldHaveSingleItem().Kind.ShouldBe(LedgerPostingKind.CreditNotePosted, "the second posting was never asked for");
        bill.BalanceDue.ShouldBe(Money.Of(126m));
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task BR210_what_the_bills_adjustments_took_off_cannot_be_credited_and_is_refused_before_a_number_is_taken()
    {
        // Review 2026-11, R1: a bill written off and then credited in full left credit, refundable as cash, for money
        // the hospital never received. One paisa adjusted off a bill of 126.00 leaves 125.99 to credit, not 126.00.
        PostedBill();
        Waiting();
        _adjustments.SumPostedAsync(BillId, Arg.Any<CancellationToken>()).Returns(Money.Of(0.01m));

        var result = await Approve().HandleAsync(new ApproveCreditNoteCommand(Id, Version(), null), CancellationToken.None);

        result.Error.Code.ShouldBe(BillingErrors.CreditNoteExceedsBillTotalCode);
        result.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        result.Error.Message.ShouldContain("taken off by adjustments");
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task BR210_an_adjustment_posted_while_the_approval_waited_for_the_lock_is_a_conflict_and_is_not_committed()
    {
        var bill = PostedBill();
        Waiting();

        // Nothing adjusted when the caps are first read; the whole bill written off once the ledger has the lock.
        var reads = 0;
        _adjustments.SumPostedAsync(BillId, Arg.Any<CancellationToken>()).Returns(_ => reads++ == 0 ? Money.Zero : Money.Of(126m));

        var result = await Approve().HandleAsync(new ApproveCreditNoteCommand(Id, Version(), null), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        reads.ShouldBe(2, "the adjustments are read again under the lock");
        _posted.ShouldHaveSingleItem().Kind.ShouldBe(LedgerPostingKind.CreditNotePosted, "the second posting was never asked for");
        bill.BalanceDue.ShouldBe(Money.Of(126m));
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task BR804_a_ledger_that_finds_the_payers_balance_going_below_zero_is_answered_as_a_conflict()
    {
        PostedBill();
        Waiting();
        _ledger.PostAsync(Arg.Is<PostingTemplate>(template => template.Kind == LedgerPostingKind.CreditApplied), Arg.Any<CancellationToken>())
            .Returns((Result<LedgerPostingId>)ReceivablesErrors.LedgerBalanceNegative());

        var result = await Approve().HandleAsync(new ApproveCreditNoteCommand(Id, Version(), null), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task BR603_the_maker_cannot_approve_their_own_credit_note_and_no_number_is_taken()
    {
        PostedBill();
        var (creditNote, request) = Waiting();
        _currentUser.UserId.Returns(Maker);

        var result = await Approve().HandleAsync(new ApproveCreditNoteCommand(Id, Version(), null), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ApprovalCheckerIsMakerCode);
        result.Error.Kind.ShouldBe(ErrorKind.Forbidden);
        creditNote.Status.ShouldBe(CreditNoteStatus.PendingApproval);
        request.Status.ShouldBe(ApprovalStatus.Pending);
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task A_stale_version_an_unknown_credit_note_and_one_that_does_not_wait_are_refused_before_anything_else()
    {
        PostedBill();
        Waiting();

        var stale = await Approve().HandleAsync(new ApproveCreditNoteCommand(Id, Convert.ToBase64String([0, 0, 0, 0, 0, 0, 7, 208]), null), CancellationToken.None);
        var unknown = await Approve().HandleAsync(new ApproveCreditNoteCommand(new CreditNoteId(Guid.NewGuid()), Version(), null), CancellationToken.None);
        _repository.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(Draft());
        var draft = await Approve().HandleAsync(new ApproveCreditNoteCommand(Id, Version(), null), CancellationToken.None);

        stale.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        unknown.Error.Code.ShouldBe(BillingErrors.CreditNoteNotFoundCode);
        draft.Error.Code.ShouldBe(BillingErrors.CreditNoteNotPendingApprovalCode);
        await NothingWasTakenOrWrittenAsync();
    }

    private ApproveCreditNoteHandler Approve() => new(
        new ApproveCreditNoteCommandValidator(), _clock, _currentUser, _bills, _repository, _adjustments, _approvals, _settings, _numbers, _ledger, _unitOfWork);

    private static string Version() => Convert.ToBase64String(CurrentVersion);

    private async Task NothingWasTakenOrWrittenAsync()
    {
        await _numbers.DidNotReceiveWithAnyArgs().NextAsync(default, default, default);
        await _ledger.DidNotReceiveWithAnyArgs().PostAsync(default!, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    // A posted bill of one line, 2 x 60.00 at 5%: 126.00, owed in full.
    private Bill PostedBill()
    {
        var bill = Bill.Create(
            Patient,
            Payer,
            new BillHeader { BillDate = TestDoubles.Today, DueDate = TestDoubles.Today.AddDays(15) },
            [new BillLineDetails { Description = "Dressing", Quantity = Quantity.Of(2m), UnitPrice = UnitPrice.Of(60m), TaxRate = Percent.Of(5m) }],
            Window).Value.WithId(BillId).Saved(CurrentVersion);
        bill.Post(DocumentNumber.Create(DocumentType.Bill, Fy, "BIL", 42), _clock.UtcNow, Maker, Window).IsSuccess.ShouldBeTrue();
        _bills.GetAsync(BillId, Arg.Any<CancellationToken>()).Returns(bill);

        return bill;
    }

    private static CreditNote Draft() => CreditNote.Create(
        BillId,
        Payer,
        Patient,
        TestDoubles.Today,
        TestDoubles.Today,
        "Dressing not done",
        [new CreditNoteLineDetails { Description = "Dressing", Quantity = Quantity.Of(2m), UnitPrice = UnitPrice.Of(60m), TaxRate = Percent.Of(5m) }],
        Window).Value.WithId(Id).Saved(CurrentVersion);

    // The credit note of these tests, waiting, with its pending request of the maker.
    private (CreditNote CreditNote, ApprovalRequest Request) Waiting()
    {
        var creditNote = Draft();
        creditNote.SubmitForApproval(TestDoubles.Today, Window).IsSuccess.ShouldBeTrue();
        var request = ApprovalRequest.Open(ApprovalDocumentType.CreditNote, Id.Value, ApprovalAction.Post, Maker, makerComment: null, _clock.UtcNow, Guid.NewGuid());
        _repository.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(creditNote);
        _approvals.GetPendingAsync(ApprovalDocumentType.CreditNote, Id.Value, Arg.Any<CancellationToken>()).Returns(request);

        return (creditNote, request);
    }

    // Another credit note of the same bill, for the whole bill, posted already.
    private CreditNote PostedElsewhere()
    {
        var other = Draft().WithId(new CreditNoteId(Guid.NewGuid()));
        other.SubmitForApproval(TestDoubles.Today, Window).IsSuccess.ShouldBeTrue();
        other.PostApproved(DocumentNumber.Create(DocumentType.CreditNote, Fy, "CN", 8), _clock.UtcNow, Checker, TestDoubles.Today, Window).IsSuccess.ShouldBeTrue();

        return other;
    }
}
