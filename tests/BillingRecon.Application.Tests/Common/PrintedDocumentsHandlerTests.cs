using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using NSubstitute;

namespace BillingRecon.Application.Tests.Common;

/// <summary>
/// The use cases behind a printed document, with the ports replaced (ADR 0054): a print is recorded with the next copy
/// number of its document, and only for a document that can be printed; the letterhead is set by its shape and under the
/// version of the settings.
/// </summary>
public sealed class PrintedDocumentsHandlerTests
{
    private static readonly Guid Document = Guid.NewGuid();
    private static readonly UserId Cashier = new(Guid.NewGuid());

    private readonly IClock _clock = TestDoubles.ClockAt(TestDoubles.Today);
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IPrintableDocuments _documents = Substitute.For<IPrintableDocuments>();
    private readonly IDocumentPrintRepository _prints = Substitute.For<IDocumentPrintRepository>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly Tenant _tenant = Tenant.Create("northfield", "Northfield Care Hospital", DateTimeOffset.UnixEpoch).Value.Saved(0, 0, 0, 0, 0, 0, 7, 209);

    public PrintedDocumentsHandlerTests()
    {
        _currentUser.UserId.Returns(Cashier);
        _documents.EnsurePrintableAsync(PrintedDocumentType.Payment, Document, Arg.Any<CancellationToken>()).Returns(Result.Success());
        _tenant.ClearDomainEvents();
        _tenants.GetCurrentAsync(Arg.Any<CancellationToken>()).Returns(_tenant);
    }

    private RecordDocumentPrintHandler Printer(IUnitOfWork unitOfWork) => new(_clock, _currentUser, _documents, _prints, unitOfWork);

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(1, 2, false)]
    [InlineData(7, 8, false)]
    public async Task ADR0054_a_print_takes_the_next_copy_number_and_only_the_first_is_the_original(int before, int copyNumber, bool original)
    {
        _prints.CountAsync(PrintedDocumentType.Payment, Document, Arg.Any<CancellationToken>()).Returns(before);
        var unitOfWork = TestDoubles.UnitOfWork();

        var result = await Printer(unitOfWork).HandleAsync(new RecordDocumentPrintCommand(PrintedDocumentType.Payment, Document), CancellationToken.None);

        result.Value.ShouldBe(new DocumentPrintDto(copyNumber, original, _clock.UtcNow));
        _prints.Received(1).Add(Arg.Is<DocumentPrint>(print =>
            print.DocumentType == PrintedDocumentType.Payment && print.DocumentId == Document && print.CopyNumber == copyNumber && print.PrintedByUserId == Cashier));
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADR0054_a_document_that_cannot_be_printed_records_nothing()
    {
        _documents.EnsurePrintableAsync(PrintedDocumentType.Bill, Document, Arg.Any<CancellationToken>()).Returns(CommonErrors.DocumentNotPrintable());
        var missing = Guid.NewGuid();
        _documents.EnsurePrintableAsync(PrintedDocumentType.Bill, missing, Arg.Any<CancellationToken>()).Returns(BillingErrors.BillNotFound());
        var unitOfWork = TestDoubles.UnitOfWork();

        var draft = await Printer(unitOfWork).HandleAsync(new RecordDocumentPrintCommand(PrintedDocumentType.Bill, Document), CancellationToken.None);
        var none = await Printer(unitOfWork).HandleAsync(new RecordDocumentPrintCommand(PrintedDocumentType.Bill, missing), CancellationToken.None);

        draft.Error.Code.ShouldBe(CommonErrors.DocumentNotPrintableCode);
        none.Error.Code.ShouldBe(BillingErrors.BillNotFoundCode);
        _prints.DidNotReceiveWithAnyArgs().Add(default!);
        await unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task ADR0054_two_prints_at_the_same_instant_cannot_take_the_same_number()
    {
        // The unique index of the copy number refused the save: the caller is told to send it again.
        var unitOfWork = TestDoubles.UnitOfWork(Error.Conflict(CommonErrors.DuplicateKeyCode, "duplicate"));

        var result = await Printer(unitOfWork).HandleAsync(new RecordDocumentPrintCommand(PrintedDocumentType.Payment, Document), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflict().Code);
    }

    private static UpdateLetterheadCommand Letterhead(string version) => new()
    {
        LegalName = "Northfield Care Hospital Private Limited",
        City = "Pune",
        State = "Maharashtra",
        PinCode = "411001",
        Gstin = " 27aaaaa0000a1z5 ",
        Signatory = "Authorised signatory",
        ExpectedVersion = version,
    };

    [Fact]
    public async Task ADR0054_the_letterhead_is_set_under_the_version_of_the_settings_with_the_gstin_in_capitals()
    {
        var unitOfWork = TestDoubles.UnitOfWork();
        var handler = new UpdateLetterheadHandler(new UpdateLetterheadCommandValidator(), _tenants, unitOfWork);

        var stale = await handler.HandleAsync(Letterhead("AAAAAAAAAAE="), CancellationToken.None);
        stale.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflict().Code);
        _tenant.Letterhead.ShouldBe(new TenantLetterhead());

        var result = await handler.HandleAsync(Letterhead(RowVersionText.Of(_tenant)), CancellationToken.None);

        result.Value.Letterhead.ShouldBe(new LetterheadDto(
            "Northfield Care Hospital", "Northfield Care Hospital Private Limited", null, null, "Pune", "Maharashtra", "411001", null, null, "27AAAAA0000A1Z5", "Authorised signatory", null, null, null));
        _tenant.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<TenantLetterheadChanged>();
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());

        // The same letterhead again is no change: nothing is saved and nothing is audited.
        await handler.HandleAsync(Letterhead(RowVersionText.Of(_tenant)), CancellationToken.None);
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        _tenant.DomainEvents.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("pinCode", "41100", "six digits")]
    [InlineData("pinCode", "4110O1", "six digits")]
    [InlineData("gstin", "27AAAAA0000A1Z", "15 characters")]
    [InlineData("gstin", "27AAAAA0000A1Z5X", "15 characters")]
    [InlineData("email", "accounts.northfield.example", "an at sign")]
    public async Task ADR0054_a_letterhead_of_the_wrong_shape_is_refused_at_its_field(string field, string value, string message)
    {
        var command = Letterhead("AAAAAAAAAAE=") with
        {
            PinCode = field == "pinCode" ? value : "411001",
            Gstin = field == "gstin" ? value : null,
            Email = field == "email" ? value : null,
        };

        var error = await new UpdateLetterheadCommandValidator().GetErrorAsync(command, CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe([field]);
        error.FieldErrors[field].ShouldHaveSingleItem().ShouldContain(message);
    }

    [Fact]
    public async Task ADR0054_anyone_signed_in_reads_the_letterhead_with_the_hospitals_name()
    {
        var letterhead = await new GetLetterheadHandler(_tenants).HandleAsync(new GetLetterheadQuery(), CancellationToken.None);

        letterhead.Value.Name.ShouldBe("Northfield Care Hospital");
        letterhead.Value.LegalName.ShouldBeNull();
    }
}
