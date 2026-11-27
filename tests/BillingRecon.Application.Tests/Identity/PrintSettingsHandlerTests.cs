using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Payments;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Domain.Payments;
using NSubstitute;

namespace BillingRecon.Application.Tests.Identity;

/// <summary>
/// The use cases of the print settings, with the ports replaced (ADR 0059): three answers with the defaults where
/// nothing was saved; a first save that must say it read none; a change under the row's version; what a bill may never
/// switch off refused with its reason; and who recorded a payment or a refund, asked of Identity by name.
/// </summary>
public sealed class PrintSettingsHandlerTests
{
    private readonly IPrintSettingsRepository _settings = Substitute.For<IPrintSettingsRepository>();

    private UpdatePrintSettingsHandler Updater(IUnitOfWork unitOfWork) => new(new UpdatePrintSettingsCommandValidator(), _settings, unitOfWork);

    private static UpdatePrintSettingsCommand Command(PrintedDocumentType documentType, PrintLayout layout, string version) => new()
    {
        DocumentType = documentType,
        Layout = layout,
        ExpectedVersion = version,
    };

    [Fact]
    public async Task ADR0059_a_hospital_that_saved_nothing_reads_three_defaults_in_order_with_the_version_of_none()
    {
        _settings.ListAsync(Arg.Any<CancellationToken>()).Returns([]);

        var result = await new GetPrintSettingsHandler(_settings).HandleAsync(new GetPrintSettingsQuery(), CancellationToken.None);

        result.Value.Select(answer => answer.DocumentType).ShouldBe([PrintedDocumentType.Bill, PrintedDocumentType.Payment, PrintedDocumentType.Refund, PrintedDocumentType.ShiftClosing]);
        result.Value.ShouldAllBe(answer => answer.Version == PrintSettingsMapping.NoVersion && answer.Paper == PaperSize.A4 && answer.MarginTopMm == 14 && answer.MarginLeftMm == 12);
        PrintSettingsMapping.NoVersion.ShouldBe("AAAAAAAAAAA=");
    }

    [Fact]
    public async Task ADR0059_a_saved_kind_answers_its_own_settings_and_the_others_their_defaults()
    {
        var receipt = PrintSettings.Create(PrintedDocumentType.Payment, PrintLayout.Default with { Paper = PaperSize.Roll80, ShowCashierName = true }).Saved(0, 0, 0, 0, 0, 0, 9, 1);
        _settings.ListAsync(Arg.Any<CancellationToken>()).Returns([receipt]);

        var result = await new GetPrintSettingsHandler(_settings).HandleAsync(new GetPrintSettingsQuery(), CancellationToken.None);

        var answers = result.Value.ToDictionary(answer => answer.DocumentType);
        (answers[PrintedDocumentType.Payment].Paper, answers[PrintedDocumentType.Payment].ShowCashierName, answers[PrintedDocumentType.Payment].Version)
            .ShouldBe((PaperSize.Roll80, true, RowVersionText.Of(receipt)));
        answers[PrintedDocumentType.Bill].Version.ShouldBe(PrintSettingsMapping.NoVersion);
        answers[PrintedDocumentType.Refund].Paper.ShouldBe(PaperSize.A4);
    }

    [Fact]
    public async Task ADR0059_the_first_save_of_a_kind_makes_its_row_and_must_say_it_read_none()
    {
        var unitOfWork = TestDoubles.UnitOfWork();
        var layout = PrintLayout.Default with { Paper = PaperSize.A5, FooterLine = "Thank you" };

        var stale = await Updater(unitOfWork).HandleAsync(Command(PrintedDocumentType.Bill, layout, "AAAAAAAAAAE="), CancellationToken.None);
        stale.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflict().Code);
        _settings.DidNotReceive().Add(Arg.Any<PrintSettings>());

        var result = await Updater(unitOfWork).HandleAsync(Command(PrintedDocumentType.Bill, layout, PrintSettingsMapping.NoVersion), CancellationToken.None);

        (result.Value.DocumentType, result.Value.Paper, result.Value.FooterLine).ShouldBe((PrintedDocumentType.Bill, PaperSize.A5, "Thank you"));
        _settings.Received(1).Add(Arg.Is<PrintSettings>(row => row.DocumentType == PrintedDocumentType.Bill && row.Layout == layout));
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADR0059_two_first_saves_at_once_let_one_through_and_tell_the_other_the_record_changed()
    {
        var unitOfWork = TestDoubles.UnitOfWork(CommonErrors.DuplicateKey());

        var result = await Updater(unitOfWork).HandleAsync(Command(PrintedDocumentType.Refund, PrintLayout.Default, PrintSettingsMapping.NoVersion), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflict().Code);
    }

    [Fact]
    public async Task ADR0059_a_change_needs_the_version_of_the_row_and_the_same_settings_again_write_nothing()
    {
        var unitOfWork = TestDoubles.UnitOfWork();
        var receipt = PrintSettings.Create(PrintedDocumentType.Payment, PrintLayout.Default).Saved(0, 0, 0, 0, 0, 0, 9, 1);
        receipt.ClearDomainEvents();
        _settings.FindAsync(PrintedDocumentType.Payment, Arg.Any<CancellationToken>()).Returns(receipt);
        var roll = PrintLayout.Default with { Paper = PaperSize.Roll80, MarginLeftMm = 5, MarginRightMm = 5 };

        // The version of settings never saved does not open a row that exists.
        var none = await Updater(unitOfWork).HandleAsync(Command(PrintedDocumentType.Payment, roll, PrintSettingsMapping.NoVersion), CancellationToken.None);
        none.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflict().Code);
        receipt.Paper.ShouldBe(PaperSize.A4);

        var same = await Updater(unitOfWork).HandleAsync(Command(PrintedDocumentType.Payment, PrintLayout.Default, RowVersionText.Of(receipt)), CancellationToken.None);
        same.IsSuccess.ShouldBeTrue();
        receipt.DomainEvents.ShouldBeEmpty();
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());

        var changed = await Updater(unitOfWork).HandleAsync(Command(PrintedDocumentType.Payment, roll, RowVersionText.Of(receipt)), CancellationToken.None);
        (changed.Value.Paper, changed.Value.MarginLeftMm).ShouldBe((PaperSize.Roll80, 5));
        receipt.DomainEvents.ShouldHaveSingleItem().ShouldBe(new PrintSettingsChanged(PrintedDocumentType.Payment));
        _settings.DidNotReceive().Add(Arg.Any<PrintSettings>());
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADR0059_what_a_bill_may_never_switch_off_and_what_is_out_of_range_is_refused_at_its_field()
    {
        var unitOfWork = TestDoubles.UnitOfWork();

        var refusals = new (PrintedDocumentType DocumentType, PrintLayout Layout, string Field, string Reason)[]
        {
            (PrintedDocumentType.Bill, PrintLayout.Default with { ShowSacColumn = false }, "showSacColumn", "cannot be switched off for a bill"),
            (PrintedDocumentType.Bill, PrintLayout.Default with { ShowTaxSplit = false }, "showTaxSplit", "cannot be switched off for a bill"),
            (PrintedDocumentType.Bill, PrintLayout.Default with { Paper = PaperSize.Roll80 }, "paper", "A bill is printed on A4 or A5"),
            (PrintedDocumentType.Payment, PrintLayout.Default with { MarginTopMm = 4 }, "marginTopMm", "between 5 and 25 mm"),
            (PrintedDocumentType.Refund, PrintLayout.Default with { MarginRightMm = 26 }, "marginRightMm", "between 5 and 25 mm"),
            (PrintedDocumentType.Refund, PrintLayout.Default with { StationeryTopMm = 81 }, "stationeryTopMm", "between 0 and 80 mm"),
            (PrintedDocumentType.Payment, PrintLayout.Default with { FooterLine = new string('x', 201) }, "footerLine", "at most 200 characters"),
            (PrintedDocumentType.Payment, PrintLayout.Default with { Terms = new string('x', 1001) }, "terms", "at most 1000 characters"),
        };

        foreach (var (documentType, layout, field, reason) in refusals)
        {
            var result = await Updater(unitOfWork).HandleAsync(Command(documentType, layout, PrintSettingsMapping.NoVersion), CancellationToken.None);

            result.Error.Code.ShouldBe("validation_failed");
            result.Error.FieldErrors.Keys.ShouldBe([field], reason);
            result.Error.FieldErrors[field].ShouldHaveSingleItem().ShouldContain(reason);
        }

        _settings.DidNotReceive().Add(Arg.Any<PrintSettings>());
        await _settings.DidNotReceive().FindAsync(Arg.Any<PrintedDocumentType>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADR0059_who_drafted_a_refund_is_read_by_name_and_no_one_where_no_user_did()
    {
        var refunds = Substitute.For<IRefundReadService>();
        var names = Substitute.For<IUserNames>();
        var id = new RefundId(Guid.NewGuid());
        var cashier = new UserId(Guid.NewGuid());
        var refund = new RefundDto(
            id.Value, Guid.NewGuid(), RefundSourceKind.Payment, Guid.NewGuid(), null, TestDoubles.Today, 200m, PaymentMethod.Cash, null, "Paid twice",
            RefundStatus.Posted, "REF/26-27/000004", "2026-27", DateTimeOffset.UnixEpoch, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "AAAAAAAACgA=");
        refunds.GetAsync(id, Arg.Any<CancellationToken>()).Returns(refund);
        names.FindDisplayNameAsync(cashier, Arg.Any<CancellationToken>()).Returns("Meera Nair");
        var handler = new GetRefundRecorderHandler(refunds, names);

        // Drafted by no user: no name, and Identity is not asked.
        refunds.FindRecorderAsync(id, Arg.Any<CancellationToken>()).Returns((UserId?)null);
        (await handler.HandleAsync(new GetRefundRecorderQuery(id), CancellationToken.None)).Value.ShouldBe(new DocumentRecorderDto(null));
        await names.DidNotReceive().FindDisplayNameAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>());

        refunds.FindRecorderAsync(id, Arg.Any<CancellationToken>()).Returns(cashier);
        (await handler.HandleAsync(new GetRefundRecorderQuery(id), CancellationToken.None)).Value.ShouldBe(new DocumentRecorderDto("Meera Nair"));

        // A refund the hospital does not have is not found, and no one is asked for a name.
        var other = new RefundId(Guid.NewGuid());
        var missing = await handler.HandleAsync(new GetRefundRecorderQuery(other), CancellationToken.None);
        missing.Error.Code.ShouldBe(PaymentsErrors.RefundNotFoundCode);
        await refunds.DidNotReceive().FindRecorderAsync(other, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADR0059_who_recorded_a_payment_is_read_by_name_and_another_payment_is_not_found()
    {
        var payments = Substitute.For<IPaymentReadService>();
        var names = Substitute.For<IUserNames>();
        var id = new PaymentId(Guid.NewGuid());
        var cashier = new UserId(Guid.NewGuid());
        var payment = new PaymentDto(
            id.Value, "PAY/26-27/000001", "2026-27", Guid.NewGuid(), TestDoubles.Today, PaymentMethod.Cash, null, 2000m, 2000m, PaymentStatus.Posted, null,
            DocumentOrigin.Standard, null, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "AAAAAAAACQA=");
        payments.GetAsync(id, Arg.Any<CancellationToken>()).Returns(payment);
        payments.FindRecorderAsync(id, Arg.Any<CancellationToken>()).Returns(cashier);
        names.FindDisplayNameAsync(cashier, Arg.Any<CancellationToken>()).Returns("Meera Nair");
        var handler = new GetPaymentRecorderHandler(payments, names);

        (await handler.HandleAsync(new GetPaymentRecorderQuery(id), CancellationToken.None)).Value.ShouldBe(new DocumentRecorderDto("Meera Nair"));

        var missing = await handler.HandleAsync(new GetPaymentRecorderQuery(new PaymentId(Guid.NewGuid())), CancellationToken.None);
        missing.Error.Code.ShouldBe(PaymentsErrors.PaymentNotFoundCode);
    }
}
