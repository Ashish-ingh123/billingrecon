using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payments;
using BillingRecon.Application.Receivables;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Domain.Payments;
using BillingRecon.Domain.Receivables;
using NSubstitute;

namespace BillingRecon.Application.Tests.Payments;

/// <summary>
/// Approving the reversal of a payment and of an allocation, with the ports replaced (US-32, US-33; BR-305, BR-306,
/// BR-603, BR-606): the approval is the reversal, the allocations are taken back before the payment, each bill is
/// reached through the Billing module's contract, and every refusal comes before anything is written.
/// </summary>
public sealed class PaymentReversalHandlerTests
{
    private static readonly byte[] CurrentVersion = [0, 0, 0, 0, 0, 0, 7, 209];
    private static readonly PayerId Payer = new(Guid.NewGuid());
    private static readonly UserId Cashier = new(Guid.NewGuid());
    private static readonly UserId Checker = new(Guid.NewGuid());
    private static readonly PaymentId Id = new(Guid.NewGuid());
    private static readonly BillId FirstBill = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));
    private static readonly BillId SecondBill = new(Guid.Parse("00000000-0000-0000-0000-000000000002"));
    private static readonly LedgerPostingId Recorded = new(Guid.NewGuid());
    private static readonly BusinessDateWindow Window = new(TestDoubles.Today, 7);

    private readonly IPaymentRepository _repository = Substitute.For<IPaymentRepository>();
    private readonly ICashierShiftRepository _shifts = Substitute.For<ICashierShiftRepository>();
    private readonly IShiftCash _shiftCash = Substitute.For<IShiftCash>();
    private readonly IApprovalRequestRepository _approvals = Substitute.For<IApprovalRequestRepository>();
    private readonly IBillSettlement _bills = Substitute.For<IBillSettlement>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IReceivablesLedger _ledger = Substitute.For<IReceivablesLedger>();
    private readonly IClock _clock = TestDoubles.ClockAt(TestDoubles.Today);
    private readonly IUnitOfWork _unitOfWork = TestDoubles.UnitOfWork();
    private readonly List<PostingTemplate> _posted = [];

    public PaymentReversalHandlerTests()
    {
        _clock.ToIstDate(Arg.Any<DateTimeOffset>()).Returns(TestDoubles.Today);
        _currentUser.UserId.Returns(Checker);
        _currentUser.Roles.Returns(new[] { Role.Supervisor });
        _ledger.PostAsync(Arg.Do<PostingTemplate>(_posted.Add), Arg.Any<CancellationToken>()).Returns(_ => (Result<LedgerPostingId>)new LedgerPostingId(Guid.NewGuid()));
        _bills.RestoreAsync(FirstBill, Payer, Arg.Any<Money>(), Arg.Any<CancellationToken>()).Returns((Result<SettledBill>)new SettledBill("BIL/26-27/000001", Money.Of(500m)));
        _bills.RestoreAsync(SecondBill, Payer, Arg.Any<Money>(), Arg.Any<CancellationToken>()).Returns((Result<SettledBill>)new SettledBill("BIL/26-27/000002", Money.Of(100m)));
    }

    [Fact]
    public async Task BR306_the_approval_reverses_the_allocations_first_then_the_payment_and_decides_the_request_in_one_commit()
    {
        var (payment, first, second) = Allocated();
        payment.RequestReversal("Cheque dishonoured").IsSuccess.ShouldBeTrue();
        var request = Pending(ApprovalDocumentType.Payment, Id.Value, "Cheque dishonoured");

        var result = await ApprovePayment().HandleAsync(new ApprovePaymentReversalCommand(Id, Version(), " Seen the bank advice "), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Payment.Status.ShouldBe(PaymentStatus.Reversed);
        result.Value.Payment.UnappliedAmount.ShouldBe(0.00m);
        result.Value.Payment.ReversalReason.ShouldBe("Cheque dishonoured", "the reason the cashier gave, kept by the request");
        result.Value.Allocations.ShouldAllBe(allocation => allocation.Status == PaymentAllocationStatus.Reversed);
        (request.Status, request.CheckerUserId, request.CheckerComment).ShouldBe((ApprovalStatus.Approved, Checker, "Seen the bank advice"));

        // Two mirror pairs, each naming the posting of its allocation, then the payment's own for the whole amount.
        _posted.Select(template => (template.Kind, template.DocumentId, template.ReversalOf)).ShouldBe(
        [
            (LedgerPostingKind.AllocationReversed, first.Id.Value, first.PostingId),
            (LedgerPostingKind.AllocationReversed, second.Id.Value, second.PostingId),
            (LedgerPostingKind.PaymentReversed, Id.Value, Recorded),
        ]);
        _posted[0].Description.ShouldBe("Allocation of payment PAY/26-27/000007 to bill BIL/26-27/000001 reversed");
        _posted.ShouldAllBe(template => template.BusinessDate == TestDoubles.Today && template.PayerId == Payer);
        first.ReversalPostingId.ShouldNotBeNull();
        payment.ReversalPostingId.ShouldNotBeNull();

        await _bills.Received(1).RestoreAsync(FirstBill, Payer, Money.Of(500m), Arg.Any<CancellationToken>());
        await _bills.Received(1).RestoreAsync(SecondBill, Payer, Money.Of(100m), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BR305_the_approval_of_an_allocation_reversal_gives_the_amount_back_to_the_payment_and_the_bill()
    {
        var (payment, first, second) = Allocated();
        payment.RequestAllocationReversal(first.Id, "Wrong bill").IsSuccess.ShouldBeTrue();
        var request = Pending(ApprovalDocumentType.PaymentAllocation, first.Id.Value, "Wrong bill");

        var result = await ApproveAllocation().HandleAsync(new ApproveAllocationReversalCommand(Id, first.Id, Version(), null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Payment.UnappliedAmount.ShouldBe(900.00m);
        result.Value.Payment.Status.ShouldBe(PaymentStatus.Posted);
        (first.Status, first.ReversalReason, first.ReversedByUserId).ShouldBe((PaymentAllocationStatus.Reversed, "Wrong bill", Checker));
        second.Status.ShouldBe(PaymentAllocationStatus.Active);
        request.Status.ShouldBe(ApprovalStatus.Approved);
        var template = _posted.ShouldHaveSingleItem();
        (template.Kind, template.DocumentType, template.ReversalOf).ShouldBe((LedgerPostingKind.AllocationReversed, LedgerDocumentType.PaymentAllocation, first.PostingId));
        await _bills.Received(1).RestoreAsync(FirstBill, Payer, Money.Of(500m), Arg.Any<CancellationToken>());
        await _bills.DidNotReceive().RestoreAsync(SecondBill, Arg.Any<PayerId>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BR603_the_cashier_who_asked_cannot_approve_and_nothing_is_written()
    {
        var (payment, _, _) = Allocated();
        payment.RequestReversal("Cheque dishonoured").IsSuccess.ShouldBeTrue();
        var request = Pending(ApprovalDocumentType.Payment, Id.Value, "Cheque dishonoured");
        _currentUser.UserId.Returns(Cashier);

        var result = await ApprovePayment().HandleAsync(new ApprovePaymentReversalCommand(Id, Version(), null), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ApprovalCheckerIsMakerCode);
        result.Error.Kind.ShouldBe(ErrorKind.Forbidden);
        payment.Status.ShouldBe(PaymentStatus.PendingReversal);
        request.Status.ShouldBe(ApprovalStatus.Pending);
        await NothingWasWrittenAsync();
    }

    [Fact]
    public async Task A_stale_version_an_unknown_payment_and_one_that_does_not_wait_are_refused_before_anything_else()
    {
        var (payment, first, _) = Allocated();

        var stale = await ApprovePayment().HandleAsync(new ApprovePaymentReversalCommand(Id, Convert.ToBase64String([0, 0, 0, 0, 0, 0, 7, 208]), null), CancellationToken.None);
        var unknown = await ApprovePayment().HandleAsync(new ApprovePaymentReversalCommand(new PaymentId(Guid.NewGuid()), Version(), null), CancellationToken.None);
        var notWaiting = await ApprovePayment().HandleAsync(new ApprovePaymentReversalCommand(Id, Version(), null), CancellationToken.None);
        var allocationNotWaiting = await ApproveAllocation().HandleAsync(new ApproveAllocationReversalCommand(Id, first.Id, Version(), null), CancellationToken.None);
        var noSuchAllocation = await ApproveAllocation().HandleAsync(new ApproveAllocationReversalCommand(Id, new PaymentAllocationId(Guid.NewGuid()), Version(), null), CancellationToken.None);

        stale.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        unknown.Error.Code.ShouldBe(PaymentsErrors.PaymentNotFoundCode);
        notWaiting.Error.Code.ShouldBe(PaymentsErrors.PaymentNotPendingReversalCode);
        allocationNotWaiting.Error.Code.ShouldBe(PaymentsErrors.AllocationNotPendingReversalCode);
        noSuchAllocation.Error.Code.ShouldBe(PaymentsErrors.AllocationNotFoundCode);
        payment.Status.ShouldBe(PaymentStatus.Posted);
        await NothingWasWrittenAsync();
    }

    private ApprovePaymentReversalHandler ApprovePayment() => new(
        new ApprovePaymentReversalCommandValidator(), _clock, _currentUser, _repository, _shifts, _shiftCash, _approvals, _bills, _ledger, _unitOfWork);

    private ApproveAllocationReversalHandler ApproveAllocation() => new(
        new ApproveAllocationReversalCommandValidator(), _clock, _currentUser, _repository, _approvals, _bills, _ledger, _unitOfWork);

    private static string Version() => Convert.ToBase64String(CurrentVersion);

    private async Task NothingWasWrittenAsync()
    {
        await _bills.DidNotReceiveWithAnyArgs().RestoreAsync(default, default, default, default);
        await _ledger.DidNotReceiveWithAnyArgs().PostAsync(default!, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    // A pending request of the cashier for a reversal, with the reason as its comment.
    private ApprovalRequest Pending(ApprovalDocumentType documentType, Guid documentId, string reason)
    {
        var requestedAt = new DateTimeOffset(TestDoubles.Today.ToDateTime(new TimeOnly(6, 0)), TimeSpan.Zero);
        var request = ApprovalRequest.Open(documentType, documentId, ApprovalAction.Reverse, Cashier, reason, requestedAt, Guid.NewGuid());
        _approvals.GetPendingAsync(documentType, documentId, Arg.Any<CancellationToken>()).Returns(request);

        return request;
    }

    // A payment of 1,000.00 with 500.00 on the first bill and 100.00 on the second, each allocation with its id and
    // its posting, as the persistence layer and the ledger would have left them.
    private (Payment Payment, PaymentAllocation First, PaymentAllocation Second) Allocated()
    {
        var number = DocumentNumber.Create(DocumentType.Payment, FiscalYear.FromDate(TestDoubles.Today), "PAY", 7);
        var payment = Payment.Record(Payer, TestDoubles.Today, PaymentMethod.Cheque, "000123", Money.Of(1000m), notes: null, number, Window).Value.WithId(Id).Saved(CurrentVersion);
        payment.AttachPosting(Recorded);
        var allocatedAt = new DateTimeOffset(TestDoubles.Today.ToDateTime(new TimeOnly(5, 0)), TimeSpan.Zero);
        var first = payment.Allocate(FirstBill, Money.Of(500m), allocatedAt, Cashier).Value.WithId(new PaymentAllocationId(Guid.NewGuid()));
        var second = payment.Allocate(SecondBill, Money.Of(100m), allocatedAt, Cashier).Value.WithId(new PaymentAllocationId(Guid.NewGuid()));
        payment.AttachAllocationPosting(first, new LedgerPostingId(Guid.NewGuid()));
        payment.AttachAllocationPosting(second, new LedgerPostingId(Guid.NewGuid()));
        _repository.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(payment);

        return (payment, first, second);
    }
}
