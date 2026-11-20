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
/// Recording a payment with its ports replaced (US-30; BR-205, BR-301, BR-302, BR-309, BR-801): the shape of the
/// request, that every refusal comes before the number is taken, and the order number, payment, ledger, commit.
/// </summary>
public sealed class RecordPaymentTests
{
    private static readonly PayerId Payer = new(Guid.NewGuid());
    private static readonly FiscalYear Fy = FiscalYear.FromDate(TestDoubles.Today);
    private static readonly LedgerPostingId Posting = new(Guid.NewGuid());

    private readonly RecordPaymentCommandValidator _validator = new();
    private readonly ListPaymentsQueryValidator _listValidator = new();
    private readonly IActivePayers _payers = Substitute.For<IActivePayers>();
    private readonly ITenantSettingsReader _settings = Substitute.For<ITenantSettingsReader>();
    private readonly IPaymentRepository _repository = Substitute.For<IPaymentRepository>();
    private readonly INumberSeries _numbers = Substitute.For<INumberSeries>();
    private readonly IReceivablesLedger _ledger = Substitute.For<IReceivablesLedger>();
    private readonly IClock _clock = TestDoubles.ClockAt(TestDoubles.Today);
    private IUnitOfWork _unitOfWork = TestDoubles.UnitOfWork();

    public RecordPaymentTests()
    {
        _clock.ToIstDate(Arg.Any<DateTimeOffset>()).Returns(TestDoubles.Today);
        _payers.EnsureActiveAsync(Payer, Arg.Any<CancellationToken>()).Returns(Result.Success());
        _settings.GetDocumentSettingsAsync(Arg.Any<CancellationToken>()).Returns(new TenantDocumentSettings(MaxBackdateDays: 7, DefaultDueDays: 15));
        _numbers.NextAsync(DocumentType.Payment, Fy, Arg.Any<CancellationToken>()).Returns(DocumentNumber.Create(DocumentType.Payment, Fy, "PAY", 17));
        _ledger.PostAsync(Arg.Any<PostingTemplate>(), Arg.Any<CancellationToken>()).Returns((Result<LedgerPostingId>)Posting);
    }

    [Fact]
    public async Task US30_a_payment_is_recorded_under_the_next_number_entered_in_the_ledger_and_committed_once()
    {
        Payment? added = null;
        _repository.Add(Arg.Do<Payment>(payment => added = payment));
        PostingTemplate? entered = null;
        await _ledger.PostAsync(Arg.Do<PostingTemplate>(template => entered = template), Arg.Any<CancellationToken>());

        var result = await Handler().HandleAsync(Upi(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Number.ShouldBe($"PAY/{Fy.ShortLabel}/000017");
        result.Value.FiscalYear.ShouldBe(Fy.Label);
        result.Value.Status.ShouldBe(PaymentStatus.Posted);
        result.Value.Method.ShouldBe(PaymentMethod.UPI);
        result.Value.Reference.ShouldBe("412345678901");
        result.Value.Amount.ShouldBe(3000.00m);
        result.Value.UnappliedAmount.ShouldBe(3000.00m);
        result.Value.PaymentDate.ShouldBe(TestDoubles.Today);
        added.ShouldNotBeNull().PostingId.ShouldBe(Posting, "the payment keeps the posting the ledger answered");

        // Dr Cash / Cr UnappliedCash(payment) for the amount, on the payment date, for the payer (BR-302).
        var template = entered.ShouldNotBeNull();
        template.Kind.ShouldBe(LedgerPostingKind.PaymentRecorded);
        template.DocumentType.ShouldBe(LedgerDocumentType.Payment);
        template.PayerId.ShouldBe(Payer);
        template.BusinessDate.ShouldBe(TestDoubles.Today);
        template.Description.ShouldBe($"Payment PAY/{Fy.ShortLabel}/000017 recorded");
        template.Lines.ShouldBe(
        [
            LedgerLine.Dr(LedgerAccount.Cash, Money.Of(3000m)),
            LedgerLine.Cr(LedgerAccount.UnappliedCash, Money.Of(3000m), new LedgerSubject(LedgerSubjectType.Payment, added.Id.Value)),
        ]);

        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BR801_the_number_is_taken_after_every_check_and_before_the_payment_the_ledger_and_the_commit()
    {
        await Handler().HandleAsync(Upi(), CancellationToken.None);

        Received.InOrder(() =>
        {
            _settings.GetDocumentSettingsAsync(Arg.Any<CancellationToken>());
            _payers.EnsureActiveAsync(Payer, Arg.Any<CancellationToken>());
            _numbers.NextAsync(DocumentType.Payment, Fy, Arg.Any<CancellationToken>());
            _repository.Add(Arg.Any<Payment>());
            _ledger.PostAsync(Arg.Any<PostingTemplate>(), Arg.Any<CancellationToken>());
            _unitOfWork.CommitAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task BR205_a_payment_without_a_date_is_dated_today_in_India_and_the_clock_is_read_once()
    {
        var result = await Handler().HandleAsync(Upi() with { PaymentDate = null }, CancellationToken.None);

        result.Value.PaymentDate.ShouldBe(TestDoubles.Today);
        _ = _clock.Received(1).UtcNow;
    }

    [Theory]
    [InlineData(1, PaymentsErrors.PaymentDateInFutureCode)]
    [InlineData(-8, PaymentsErrors.PaymentDateTooOldCode)]
    public async Task BR205_a_date_that_is_not_allowed_today_is_refused_and_no_number_is_taken(int daysFromToday, string code)
    {
        var result = await Handler().HandleAsync(Upi() with { PaymentDate = TestDoubles.Today.AddDays(daysFromToday) }, CancellationToken.None);

        result.Error.Code.ShouldBe(code);
        await _payers.DidNotReceiveWithAnyArgs().EnsureActiveAsync(default, default);
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task BR309_a_payment_for_an_inactive_or_unknown_payer_is_refused_and_no_number_is_taken()
    {
        _payers.EnsureActiveAsync(Payer, Arg.Any<CancellationToken>()).Returns(PayersErrors.PayerInactive());

        var inactive = await Handler().HandleAsync(Upi(), CancellationToken.None);

        inactive.Error.Code.ShouldBe(PayersErrors.PayerInactiveCode);
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task US25_a_ledger_that_refuses_or_fails_leaves_the_commit_undone()
    {
        _ledger.PostAsync(Arg.Any<PostingTemplate>(), Arg.Any<CancellationToken>()).Returns((Result<LedgerPostingId>)ReceivablesErrors.LedgerBalanceNegative());

        var refused = await Handler().HandleAsync(Upi(), CancellationToken.None);

        // The number was taken inside the transaction; without a commit it goes back with the rest (BR-801).
        refused.Error.Code.ShouldBe(ReceivablesErrors.LedgerBalanceNegativeCode);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);

        _ledger.PostAsync(Arg.Any<PostingTemplate>(), Arg.Any<CancellationToken>()).Returns<Task<Result<LedgerPostingId>>>(_ => throw new InvalidOperationException("The ledger failed."));

        await Should.ThrowAsync<InvalidOperationException>(() => Handler().HandleAsync(Upi(), CancellationToken.None));
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task A_commit_that_fails_answers_its_error()
    {
        _unitOfWork = TestDoubles.UnitOfWork(CommonErrors.DuplicateKey());

        var result = await Handler().HandleAsync(Upi(), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.DuplicateKeyCode);
    }

    [Fact]
    public async Task A_malformed_request_is_refused_before_anything_is_read()
    {
        var result = await Handler().HandleAsync(Upi() with { Amount = 0m }, CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        await _settings.DidNotReceiveWithAnyArgs().GetDocumentSettingsAsync(default);
        await NothingWasTakenOrWrittenAsync();
    }

    [Fact]
    public async Task A_complete_request_is_valid_and_cash_needs_only_a_payer_and_an_amount()
    {
        (await _validator.GetErrorAsync(Upi(), CancellationToken.None)).ShouldBeNull();
        (await _validator.GetErrorAsync(new RecordPaymentCommand { PayerId = Payer, Method = PaymentMethod.Cash, Amount = 0.01m }, CancellationToken.None)).ShouldBeNull();
        (await _validator.GetErrorAsync(Upi() with { Amount = Payment.MaxAmount }, CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData(PaymentMethod.Card)]
    [InlineData(PaymentMethod.UPI)]
    [InlineData(PaymentMethod.BankTransfer)]
    [InlineData(PaymentMethod.Cheque)]
    public async Task BR301_a_reference_is_required_for_every_method_except_cash(PaymentMethod method)
    {
        await ShouldRefuseAsync(Upi() with { Method = method, Reference = null }, "reference");
        await ShouldRefuseAsync(Upi() with { Method = method, Reference = "   " }, "reference");
        (await _validator.GetErrorAsync(Upi() with { Method = method, Reference = "REF-1" }, CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.001)]
    [InlineData(100.005)]
    [InlineData(10000000000)]
    public async Task BR301_an_amount_that_is_not_above_zero_not_exact_to_the_paisa_or_too_large_is_refused(decimal amount)
    {
        await ShouldRefuseAsync(Upi() with { Amount = amount }, "amount");
    }

    [Fact]
    public async Task A_missing_payer_method_or_amount_an_unknown_method_and_texts_that_are_too_long_are_refused()
    {
        await ShouldRefuseAsync(Upi() with { PayerId = default }, "payerId");
        await ShouldRefuseAsync(Upi() with { Method = null }, "method");
        await ShouldRefuseAsync(Upi() with { Method = (PaymentMethod)99 }, "method");
        await ShouldRefuseAsync(Upi() with { Amount = null }, "amount");
        await ShouldRefuseAsync(Upi() with { Reference = new string('r', Payment.ReferenceMaxLength + 1) }, "reference");
        await ShouldRefuseAsync(Upi() with { Notes = new string('n', Payment.NotesMaxLength + 1) }, "notes");
    }

    [Fact]
    public async Task The_list_takes_a_page_within_the_limits_known_filters_and_a_range_that_does_not_end_before_it_starts()
    {
        (await _listValidator.GetErrorAsync(new ListPaymentsQuery(), CancellationToken.None)).ShouldBeNull();
        (await _listValidator.GetErrorAsync(new ListPaymentsQuery { PayerId = Payer, Method = PaymentMethod.Cheque, Status = PaymentStatus.Posted, PaymentDateFrom = TestDoubles.Today, PaymentDateTo = TestDoubles.Today }, CancellationToken.None)).ShouldBeNull();

        (await _listValidator.GetErrorAsync(new ListPaymentsQuery { PaymentDateFrom = TestDoubles.Today, PaymentDateTo = TestDoubles.Today.AddDays(-1) }, CancellationToken.None))
            .ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["paymentDateTo"]);
        (await _listValidator.GetErrorAsync(new ListPaymentsQuery { Method = (PaymentMethod)99 }, CancellationToken.None)).ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["method"]);
        (await _listValidator.GetErrorAsync(new ListPaymentsQuery { PayerId = default(PayerId) }, CancellationToken.None)).ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["payerId"]);
        (await _listValidator.GetErrorAsync(new ListPaymentsQuery { PageSize = Paging.MaxPageSize + 1 }, CancellationToken.None)).ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["pageSize"]);
    }

    [Fact]
    public async Task Reading_a_payment_that_does_not_exist_is_payment_not_found()
    {
        var payments = Substitute.For<IPaymentReadService>();

        var result = await new GetPaymentHandler(payments).HandleAsync(new GetPaymentQuery(new PaymentId(Guid.NewGuid())), CancellationToken.None);

        result.Error.Code.ShouldBe(PaymentsErrors.PaymentNotFoundCode);
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    private async Task ShouldRefuseAsync(RecordPaymentCommand command, string field)
    {
        var error = await _validator.GetErrorAsync(command, CancellationToken.None);

        error.ShouldNotBeNull($"the command should have been refused on {field}");
        error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        error.FieldErrors.Keys.ShouldBe([field]);
    }

    private async Task NothingWasTakenOrWrittenAsync()
    {
        await _numbers.DidNotReceiveWithAnyArgs().NextAsync(default, default, default);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        await _ledger.DidNotReceiveWithAnyArgs().PostAsync(default!, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    private RecordPaymentHandler Handler() => new(_validator, _clock, _repository, _settings, _payers, _numbers, _ledger, _unitOfWork);

    private static RecordPaymentCommand Upi() => new()
    {
        PayerId = Payer,
        PaymentDate = TestDoubles.Today,
        Method = PaymentMethod.UPI,
        Reference = "412345678901",
        Amount = 3000m,
        Notes = "Paid at the counter",
    };
}
