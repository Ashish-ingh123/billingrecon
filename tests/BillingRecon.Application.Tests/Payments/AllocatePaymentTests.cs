using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payments;
using BillingRecon.Application.Receivables;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using BillingRecon.Domain.Receivables;
using NSubstitute;

namespace BillingRecon.Application.Tests.Payments;

/// <summary>
/// Allocating a payment with its ports replaced (US-31; BR-303, BR-304, BR-804): the shape of the request, that
/// every refusal comes before the ledger, the order bills, ledger, commit, and that the bills are taken in the order
/// of their ids.
/// </summary>
public sealed class AllocatePaymentTests
{
    private static readonly byte[] CurrentVersion = [0, 0, 0, 0, 0, 0, 7, 209];
    private static readonly byte[] OlderVersion = [0, 0, 0, 0, 0, 0, 7, 208];
    private static readonly PaymentId Id = new(Guid.NewGuid());
    private static readonly PayerId Payer = new(Guid.NewGuid());
    private static readonly UserId Cashier = new(Guid.NewGuid());
    private static readonly BillId LowBill = new(Guid.Parse("00000000-0000-0000-0000-00000000000a"));
    private static readonly BillId HighBill = new(Guid.Parse("00000000-0000-0000-0000-00000000000b"));

    private readonly AllocatePaymentCommandValidator _validator = new();
    private readonly IPaymentRepository _repository = Substitute.For<IPaymentRepository>();
    private readonly IBillSettlement _bills = Substitute.For<IBillSettlement>();
    private readonly IReceivablesLedger _ledger = Substitute.For<IReceivablesLedger>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IClock _clock = TestDoubles.ClockAt(TestDoubles.Today);
    private readonly List<PostingTemplate> _entered = [];
    private IUnitOfWork _unitOfWork = TestDoubles.UnitOfWork();

    public AllocatePaymentTests()
    {
        _clock.ToIstDate(Arg.Any<DateTimeOffset>()).Returns(TestDoubles.Today);
        _currentUser.UserId.Returns(Cashier);
        _bills.ApplyAsync(LowBill, Payer, Arg.Any<Money>(), Arg.Any<CancellationToken>()).Returns(call => (Result<SettledBill>)new SettledBill("BIL/26-27/000001", Money.Zero));
        _bills.ApplyAsync(HighBill, Payer, Arg.Any<Money>(), Arg.Any<CancellationToken>()).Returns(call => (Result<SettledBill>)new SettledBill("BIL/26-27/000002", Money.Of(10m)));
        _ledger.PostAsync(Arg.Do<PostingTemplate>(_entered.Add), Arg.Any<CancellationToken>()).Returns(call => (Result<LedgerPostingId>)new LedgerPostingId(Guid.NewGuid()));

        // The persistence layer gives an allocation its id when it is added.
        _repository.Add(Arg.Do<PaymentAllocation>(allocation => allocation.WithId(new PaymentAllocationId(Guid.NewGuid()))));
    }

    [Fact]
    public async Task US31_a_payment_is_allocated_to_two_bills_each_entered_in_the_ledger_and_committed_once()
    {
        var payment = Stored(3000m);

        var result = await Handler().HandleAsync(Command((HighBill, 700m), (LowBill, 1300.50m)), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Payment.UnappliedAmount.ShouldBe(999.50m);
        result.Value.Payment.Amount.ShouldBe(3000m);
        result.Value.Allocations.Select(allocation => (allocation.BillId, allocation.Amount)).ShouldBe([(LowBill.Value, 1300.50m), (HighBill.Value, 700m)], ignoreOrder: true);
        result.Value.Allocations.ShouldAllBe(allocation => allocation.Status == PaymentAllocationStatus.Active && allocation.AllocatedByUserId == Cashier.Value && allocation.AllocatedAtUtc == _clock.UtcNow);

        // One posting per allocation: Dr UnappliedCash(payment) / Cr Receivable(bill), dated today, for the payer.
        _entered.Count.ShouldBe(2);
        var first = _entered[0];
        first.Kind.ShouldBe(LedgerPostingKind.PaymentAllocated);
        first.DocumentType.ShouldBe(LedgerDocumentType.PaymentAllocation);
        first.DocumentId.ShouldBe(payment.Allocations.Single(allocation => allocation.BillId == LowBill).Id.Value);
        first.PayerId.ShouldBe(Payer);
        first.BusinessDate.ShouldBe(TestDoubles.Today);
        first.Description.ShouldBe("Payment PAY/26-27/000017 allocated to bill BIL/26-27/000001");
        first.Lines.ShouldBe(
        [
            LedgerLine.Dr(LedgerAccount.UnappliedCash, Money.Of(1300.50m), new LedgerSubject(LedgerSubjectType.Payment, Id.Value)),
            LedgerLine.Cr(LedgerAccount.Receivable, Money.Of(1300.50m), new LedgerSubject(LedgerSubjectType.Bill, LowBill.Value)),
        ]);
        _entered[1].Description.ShouldBe("Payment PAY/26-27/000017 allocated to bill BIL/26-27/000002");
        payment.Allocations.ShouldAllBe(allocation => allocation.PostingId.Value != Guid.Empty, "every allocation keeps the posting the ledger answered");
        payment.Allocations.Select(allocation => allocation.PostingId).Distinct().Count().ShouldBe(2);

        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_bills_are_taken_in_the_order_of_their_ids_and_all_of_them_before_the_ledger_and_the_commit()
    {
        Stored(3000m);

        await Handler().HandleAsync(Command((HighBill, 700m), (LowBill, 1300m)), CancellationToken.None);

        Received.InOrder(() =>
        {
            _bills.ApplyAsync(LowBill, Payer, Money.Of(1300m), Arg.Any<CancellationToken>());
            _bills.ApplyAsync(HighBill, Payer, Money.Of(700m), Arg.Any<CancellationToken>());
            _ledger.PostAsync(Arg.Any<PostingTemplate>(), Arg.Any<CancellationToken>());
            _ledger.PostAsync(Arg.Any<PostingTemplate>(), Arg.Any<CancellationToken>());
            _unitOfWork.CommitAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task A_payment_the_tenant_does_not_have_is_not_found()
    {
        var result = await Handler().HandleAsync(Command((LowBill, 100m)), CancellationToken.None);

        result.Error.Code.ShouldBe(PaymentsErrors.PaymentNotFoundCode);
        await NothingWasEnteredOrCommittedAsync();
    }

    [Fact]
    public async Task BR804_a_version_older_than_the_payment_is_a_conflict_and_no_bill_is_touched()
    {
        Stored(3000m);

        var result = await Handler().HandleAsync(Command((LowBill, 100m)) with { ExpectedVersion = Convert.ToBase64String(OlderVersion) }, CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        await _bills.DidNotReceiveWithAnyArgs().ApplyAsync(default, default, default, default);
        await NothingWasEnteredOrCommittedAsync();
    }

    [Fact]
    public async Task BR303_a_request_whose_sum_is_more_than_the_payment_has_unapplied_is_refused_before_any_bill_is_touched()
    {
        var payment = Stored(2000m);

        var result = await Handler().HandleAsync(Command((LowBill, 1500m), (HighBill, 500.01m)), CancellationToken.None);

        result.Error.Code.ShouldBe(PaymentsErrors.AllocationExceedsUnappliedCode);
        payment.UnappliedAmount.ShouldBe(Money.Of(2000m));
        await _bills.DidNotReceiveWithAnyArgs().ApplyAsync(default, default, default, default);
        await NothingWasEnteredOrCommittedAsync();
    }

    [Theory]
    [InlineData(BillingErrors.BillNotFoundCode)]
    [InlineData(BillingErrors.BillNotPostedCode)]
    [InlineData(BillingErrors.AllocationPayerMismatchCode)]
    [InlineData(BillingErrors.AllocationExceedsBalanceDueCode)]
    public async Task BR303_a_bill_that_refuses_ends_the_request_with_its_answer_and_nothing_is_entered_or_committed(string code)
    {
        Stored(3000m);
        _bills.ApplyAsync(HighBill, Payer, Arg.Any<Money>(), Arg.Any<CancellationToken>()).Returns((Result<SettledBill>)Error.BusinessRule(code, "Refused."));

        var result = await Handler().HandleAsync(Command((LowBill, 100m), (HighBill, 100m)), CancellationToken.None);

        result.Error.Code.ShouldBe(code);
        await NothingWasEnteredOrCommittedAsync();
    }

    [Fact]
    public async Task BR804_a_ledger_that_finds_a_balance_below_zero_is_a_stale_read_and_any_ledger_failure_leaves_the_commit_undone()
    {
        Stored(3000m);
        _ledger.PostAsync(Arg.Any<PostingTemplate>(), Arg.Any<CancellationToken>()).Returns((Result<LedgerPostingId>)ReceivablesErrors.LedgerBalanceNegative());

        var refused = await Handler().HandleAsync(Command((LowBill, 100m)), CancellationToken.None);

        // The documents accepted the amount as they were read; the ledger saw what somebody committed since.
        refused.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        refused.Error.Kind.ShouldBe(ErrorKind.Conflict);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);

        Stored(3000m);
        _ledger.PostAsync(Arg.Any<PostingTemplate>(), Arg.Any<CancellationToken>()).Returns<Task<Result<LedgerPostingId>>>(_ => throw new InvalidOperationException("The ledger failed."));

        await Should.ThrowAsync<InvalidOperationException>(() => Handler().HandleAsync(Command((LowBill, 100m)), CancellationToken.None));
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task BR804_a_commit_that_finds_a_bill_or_the_payment_changed_answers_the_conflict()
    {
        Stored(3000m);
        _unitOfWork = TestDoubles.UnitOfWork(CommonErrors.ConcurrencyConflict());

        var result = await Handler().HandleAsync(Command((LowBill, 100m)), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
    }

    [Fact]
    public async Task A_malformed_request_is_refused_before_the_payment_is_read()
    {
        var result = await Handler().HandleAsync(Command(), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        await _repository.DidNotReceiveWithAnyArgs().GetAsync(default, default);
        await NothingWasEnteredOrCommittedAsync();
    }

    [Fact]
    public async Task A_request_names_between_one_and_fifty_bills_each_of_them_once()
    {
        (await _validator.GetErrorAsync(Command((LowBill, 0.01m)), CancellationToken.None)).ShouldBeNull();
        (await _validator.GetErrorAsync(Many(AllocatePaymentCommand.MaxAllocations), CancellationToken.None)).ShouldBeNull();

        await ShouldRefuseAsync(Command(), "allocations");
        await ShouldRefuseAsync(Many(AllocatePaymentCommand.MaxAllocations + 1), "allocations");
        await ShouldRefuseAsync(Command((LowBill, 100m), (HighBill, 50m), (LowBill, 25m)), "allocations");
        await ShouldRefuseAsync(Command((LowBill, 100m)) with { ExpectedVersion = string.Empty }, "expectedVersion");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.001)]
    [InlineData(100.005)]
    [InlineData(10000000000)]
    public async Task BR303_an_amount_that_is_not_above_zero_not_exact_to_the_paisa_or_too_large_is_refused_on_its_line(decimal amount)
    {
        await ShouldRefuseAsync(Command((LowBill, 100m), (HighBill, amount)), "allocations[1].amount");
    }

    [Fact]
    public async Task A_line_without_a_bill_or_an_amount_and_a_null_line_are_refused_each_on_its_own_field()
    {
        await ShouldRefuseAsync(Command((default, 100m)), "allocations[0].billId");
        await ShouldRefuseAsync(new AllocatePaymentCommand { Id = Id, ExpectedVersion = Version(), Allocations = [new AllocationInput(LowBill, null)] }, "allocations[0].amount");
        await ShouldRefuseAsync(new AllocatePaymentCommand { Id = Id, ExpectedVersion = Version(), Allocations = [new AllocationInput(LowBill, 1m), null!] }, "allocations[1]");

        // Every line is checked, so a cashier corrects them in one go.
        var error = await _validator.GetErrorAsync(Command((default, 0m), (HighBill, -1m)), CancellationToken.None);
        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["allocations[0].billId", "allocations[0].amount", "allocations[1].amount"], ignoreOrder: true);
    }

    [Fact]
    public async Task Reading_the_allocations_of_a_payment_that_does_not_exist_is_payment_not_found()
    {
        var payments = Substitute.For<IPaymentReadService>();

        var result = await new GetPaymentAllocationsHandler(payments).HandleAsync(new GetPaymentAllocationsQuery(Id), CancellationToken.None);

        result.Error.Code.ShouldBe(PaymentsErrors.PaymentNotFoundCode);
    }

    private async Task ShouldRefuseAsync(AllocatePaymentCommand command, string field)
    {
        var error = await _validator.GetErrorAsync(command, CancellationToken.None);

        error.ShouldNotBeNull($"the command should have been refused on {field}");
        error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        error.FieldErrors.Keys.ShouldBe([field]);
    }

    private async Task NothingWasEnteredOrCommittedAsync()
    {
        await _ledger.DidNotReceiveWithAnyArgs().PostAsync(default!, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    private AllocatePaymentHandler Handler() => new(_validator, _clock, _currentUser, _repository, _bills, _ledger, _unitOfWork);

    private Payment Stored(decimal amount)
    {
        var number = DocumentNumber.Create(DocumentType.Payment, FiscalYear.FromDate(TestDoubles.Today), "PAY", 17);
        var payment = Payment.Record(Payer, TestDoubles.Today, PaymentMethod.Cash, null, Money.Of(amount), null, number, new BusinessDateWindow(TestDoubles.Today, 7)).Value;
        payment.Saved(CurrentVersion).WithId(Id);
        _repository.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(payment);

        return payment;
    }

    private static string Version() => Convert.ToBase64String(CurrentVersion);

    private static AllocatePaymentCommand Command(params (BillId BillId, decimal Amount)[] lines) => new()
    {
        Id = Id,
        ExpectedVersion = Version(),
        Allocations = [.. lines.Select(line => new AllocationInput(line.BillId, line.Amount))],
    };

    private static AllocatePaymentCommand Many(int count) => new()
    {
        Id = Id,
        ExpectedVersion = Version(),
        Allocations = [.. Enumerable.Range(1, count).Select(_ => new AllocationInput(new BillId(Guid.NewGuid()), 1m))],
    };
}
