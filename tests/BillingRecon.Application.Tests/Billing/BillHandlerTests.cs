using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using NSubstitute;

namespace BillingRecon.Application.Tests.Billing;

/// <summary>
/// The bill use cases with their ports replaced (BR-102, BR-105, BR-201 to BR-205, BR-804): what they check, in which
/// order, where the defaults come from, and that nothing is written when a check fails.
/// </summary>
public sealed class BillHandlerTests
{
    private static readonly byte[] CurrentVersion = [0, 0, 0, 0, 0, 0, 7, 209];
    private static readonly byte[] OlderVersion = [0, 0, 0, 0, 0, 0, 7, 208];
    private static readonly PatientId Patient = new(Guid.NewGuid());
    private static readonly PayerId Payer = new(Guid.NewGuid());
    private static readonly UserId Clerk = new(Guid.NewGuid());

    private readonly IBillableParties _parties = Substitute.For<IBillableParties>();
    private readonly ITenantSettingsReader _settings = Substitute.For<ITenantSettingsReader>();
    private readonly IServiceItemReadService _items = Substitute.For<IServiceItemReadService>();
    private readonly IBillRepository _repository = Substitute.For<IBillRepository>();
    private readonly IBillReadService _bills = Substitute.For<IBillReadService>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IClock _clock = TestDoubles.ClockAt(TestDoubles.Today);
    private readonly IUnitOfWork _unitOfWork = TestDoubles.UnitOfWork();

    public BillHandlerTests()
    {
        _parties.EnsureCanBeBilledAsync(Patient, Payer, Arg.Any<CancellationToken>()).Returns(Result.Success());
        _settings.GetDocumentSettingsAsync(Arg.Any<CancellationToken>()).Returns(new TenantDocumentSettings(MaxBackdateDays: 7, DefaultDueDays: 15));
        _items.GetManyAsync(Arg.Any<IReadOnlyCollection<ServiceItemId>>(), Arg.Any<CancellationToken>()).Returns([]);
        _currentUser.UserId.Returns(Clerk);
    }

    [Fact]
    public async Task US20_a_draft_is_created_for_the_patient_and_payer_and_committed_once()
    {
        var result = await Create().HandleAsync(NewDraft(FreeText()), CancellationToken.None);

        result.Value.Status.ShouldBe(BillStatus.Draft);
        result.Value.PatientId.ShouldBe(Patient.Value);
        result.Value.PayerId.ShouldBe(Payer.Value);
        result.Value.Lines.ShouldHaveSingleItem().LineTotal.ShouldBe(126.00m);
        result.Value.Total.ShouldBe(126.00m);
        result.Value.BalanceDue.ShouldBe(126.00m);
        _repository.Received(1).Add(Arg.Any<Bill>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BR205_a_draft_without_dates_is_dated_today_in_India_and_due_after_the_tenants_default_days()
    {
        var result = await Create().HandleAsync(NewDraft(), CancellationToken.None);

        result.Value.BillDate.ShouldBe(TestDoubles.Today);
        result.Value.DueDate.ShouldBe(TestDoubles.Today.AddDays(15));
    }

    [Fact]
    public async Task BR205_dates_the_client_sends_are_kept_and_the_due_date_defaults_from_the_bill_date_sent()
    {
        var backdated = TestDoubles.Today.AddDays(-3);

        var both = await Create().HandleAsync(NewDraft() with { BillDate = backdated, DueDate = backdated.AddDays(1) }, CancellationToken.None);
        var billDateOnly = await Create().HandleAsync(NewDraft() with { BillDate = backdated }, CancellationToken.None);

        both.Value.DueDate.ShouldBe(backdated.AddDays(1));
        billDateOnly.Value.BillDate.ShouldBe(backdated);
        billDateOnly.Value.DueDate.ShouldBe(backdated.AddDays(15));
    }

    [Theory]
    [InlineData(1, BillingErrors.BillDateInFutureCode)]
    [InlineData(-8, BillingErrors.BillDateTooOldCode)]
    public async Task BR205_a_bill_date_outside_what_the_tenant_allows_is_refused_with_422_and_nothing_is_written(int daysFromToday, string errorCode)
    {
        var result = await Create().HandleAsync(NewDraft() with { BillDate = TestDoubles.Today.AddDays(daysFromToday) }, CancellationToken.None);

        result.Error.Code.ShouldBe(errorCode);
        result.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task BR205_the_last_day_of_the_calendar_is_refused_as_a_future_date_and_not_as_a_fault()
    {
        var result = await Create().HandleAsync(NewDraft() with { BillDate = DateOnly.MaxValue }, CancellationToken.None);

        result.Error.Code.ShouldBe(BillingErrors.BillDateInFutureCode);
    }

    [Fact]
    public async Task An_invalid_draft_is_refused_with_400_before_anything_is_read()
    {
        var result = await Create().HandleAsync(NewDraft(FreeText() with { Quantity = 0m }), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        result.Error.FieldErrors.Keys.ShouldBe(["lines[0].quantity"]);
        await _parties.DidNotReceiveWithAnyArgs().EnsureCanBeBilledAsync(default, default, default);
    }

    [Theory]
    [InlineData(PayersErrors.PatientNotFoundCode)]
    [InlineData(PayersErrors.PatientInactiveCode)]
    [InlineData(PayersErrors.PayerNotFoundCode)]
    [InlineData(PayersErrors.PayerInactiveCode)]
    public async Task BR102_BR105_what_the_payers_module_answers_about_the_patient_and_the_payer_is_passed_on_unchanged(string errorCode)
    {
        var refusal = Error.BusinessRule(errorCode, "As the Payers module worded it.");
        _parties.EnsureCanBeBilledAsync(Patient, Payer, Arg.Any<CancellationToken>()).Returns(Result.Failure(refusal));

        var result = await Create().HandleAsync(NewDraft(FreeText()), CancellationToken.None);

        result.Error.ShouldBeSameAs(refusal);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task BR201_a_line_from_the_catalogue_takes_the_description_the_price_and_the_tax_rate_it_does_not_send()
    {
        var item = InCatalogue("OPD consultation", 500m, 18m);

        var result = await Create().HandleAsync(NewDraft(new BillLineInput { ServiceItemId = item, Quantity = 2m }), CancellationToken.None);

        var line = result.Value.Lines.ShouldHaveSingleItem();
        line.ServiceItemId.ShouldBe(item.Value);
        line.Description.ShouldBe("OPD consultation");
        line.UnitPrice.ShouldBe(500m);
        line.TaxRatePercent.ShouldBe(18m);
        line.LineAmount.ShouldBe(1000.00m);
        line.TaxAmount.ShouldBe(180.00m);
        line.LineTotal.ShouldBe(1180.00m);
    }

    [Fact]
    public async Task BR201_what_a_catalogue_line_does_send_replaces_the_default_including_a_price_and_a_tax_rate_of_zero()
    {
        var item = InCatalogue("OPD consultation", 500m, 18m);
        var line = new BillLineInput { ServiceItemId = item, Description = "Consultation, staff rate", Quantity = 1m, UnitPrice = 0m, TaxRatePercent = 0m };

        var result = await Create().HandleAsync(NewDraft(line), CancellationToken.None);

        var created = result.Value.Lines.ShouldHaveSingleItem();
        created.Description.ShouldBe("Consultation, staff rate");
        created.UnitPrice.ShouldBe(0m);
        created.TaxRatePercent.ShouldBe(0m);
        created.LineTotal.ShouldBe(0m);
    }

    [Fact]
    public async Task The_catalogue_is_read_once_for_all_lines_and_not_at_all_for_free_text()
    {
        var item = InCatalogue("OPD consultation", 500m, 18m);
        var fromCatalogue = new BillLineInput { ServiceItemId = item, Quantity = 1m };

        await Create().HandleAsync(NewDraft(fromCatalogue, FreeText(), fromCatalogue), CancellationToken.None);
        await _items.Received(1).GetManyAsync(Arg.Is<IReadOnlyCollection<ServiceItemId>>(ids => ids.Count == 1), Arg.Any<CancellationToken>());

        _items.ClearReceivedCalls();
        await Create().HandleAsync(NewDraft(FreeText()), CancellationToken.None);
        await _items.DidNotReceiveWithAnyArgs().GetManyAsync(default!, default);
    }

    [Fact]
    public async Task A_line_for_a_service_item_the_tenant_does_not_have_is_not_found()
    {
        var result = await Create().HandleAsync(NewDraft(new BillLineInput { ServiceItemId = new ServiceItemId(Guid.NewGuid()), Quantity = 1m }), CancellationToken.None);

        result.Error.Code.ShouldBe(BillingErrors.ServiceItemNotFoundCode);
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task A_new_line_for_an_inactive_service_item_is_refused_with_422()
    {
        var item = InCatalogue("Retired service", 100m, 0m, ServiceItemStatus.Inactive);

        var result = await Create().HandleAsync(NewDraft(new BillLineInput { ServiceItemId = item, Quantity = 1m }), CancellationToken.None);

        result.Error.Code.ShouldBe(BillingErrors.ServiceItemInactiveCode);
        result.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
    }

    [Fact]
    public async Task BR201_a_discount_larger_than_its_line_is_refused_with_422_naming_the_line()
    {
        var result = await Create().HandleAsync(NewDraft(FreeText(), FreeText() with { DiscountAmount = 120.01m }), CancellationToken.None);

        result.Error.Code.ShouldBe(BillingErrors.BillLineDiscountExceedsAmountCode);
        result.Error.Message.ShouldContain("line 2");
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task BR204_replacing_the_lines_of_a_draft_from_the_current_version_recomputes_it_and_commits()
    {
        var bill = Existing(CurrentVersion);

        var result = await Replace().HandleAsync(Lines(bill.Id, CurrentVersion, FreeText(), FreeText() with { Quantity = 1m }), CancellationToken.None);

        result.Value.Lines.Select(line => line.LineNo).ShouldBe([1, 2]);
        result.Value.Total.ShouldBe(126.00m + 63.00m);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BR804_replacing_lines_from_a_stale_version_is_a_conflict_and_changes_nothing()
    {
        var bill = Existing(CurrentVersion);

        var result = await Replace().HandleAsync(Lines(bill.Id, OlderVersion), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        bill.Lines.ShouldHaveSingleItem();
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task Replacing_the_lines_of_an_unknown_bill_is_not_found_and_of_a_voided_one_is_refused_before_the_catalogue_is_read()
    {
        var voided = Existing(CurrentVersion);
        voided.Void("Duplicate", _clock.UtcNow, Clerk);

        (await Replace().HandleAsync(Lines(new BillId(Guid.NewGuid()), CurrentVersion), CancellationToken.None)).Error.Code.ShouldBe(BillingErrors.BillNotFoundCode);
        (await Replace().HandleAsync(Lines(voided.Id, CurrentVersion, new BillLineInput { ServiceItemId = new ServiceItemId(Guid.NewGuid()), Quantity = 1m }), CancellationToken.None))
            .Error.Code.ShouldBe(BillingErrors.BillNotDraftCode);
        await _items.DidNotReceiveWithAnyArgs().GetManyAsync(default!, default);
    }

    [Fact]
    public async Task A_service_item_deactivated_since_may_stay_on_the_draft_that_already_has_it_but_not_join_another()
    {
        var item = InCatalogue("OPD consultation", 500m, 18m);
        var withItem = ExistingWith(CurrentVersion, new BillLineDetails
        {
            ServiceItemId = item,
            Description = "OPD consultation",
            Quantity = Quantity.Of(1m),
            UnitPrice = UnitPrice.Of(500m),
            TaxRate = Percent.Of(18m),
        });
        var withoutItem = Existing(CurrentVersion);
        InCatalogue(item, "OPD consultation", 500m, 18m, ServiceItemStatus.Inactive);
        var line = new BillLineInput { ServiceItemId = item, Quantity = 3m };

        (await Replace().HandleAsync(Lines(withItem.Id, CurrentVersion, line), CancellationToken.None)).Value.Lines.ShouldHaveSingleItem().Quantity.ShouldBe(3m);
        (await Replace().HandleAsync(Lines(withoutItem.Id, CurrentVersion, line), CancellationToken.None)).Error.Code.ShouldBe(BillingErrors.ServiceItemInactiveCode);
    }

    [Fact]
    public async Task BR204_correcting_the_header_from_the_current_version_replaces_it_and_commits()
    {
        var bill = Existing(CurrentVersion);
        var billDate = TestDoubles.Today.AddDays(-1);

        var result = await Header().HandleAsync(
            new UpdateBillHeaderCommand { Id = bill.Id, ExpectedVersion = Text(CurrentVersion), BillDate = billDate, DueDate = billDate.AddDays(30), Notes = "Follow-up" },
            CancellationToken.None);

        result.Value.BillDate.ShouldBe(billDate);
        result.Value.DueDate.ShouldBe(billDate.AddDays(30));
        result.Value.Notes.ShouldBe("Follow-up");
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Correcting_the_header_checks_the_shape_the_bill_the_version_and_the_dates_in_that_order()
    {
        var bill = Existing(CurrentVersion);
        var command = new UpdateBillHeaderCommand { Id = bill.Id, ExpectedVersion = Text(CurrentVersion), BillDate = TestDoubles.Today, DueDate = TestDoubles.Today };

        (await Header().HandleAsync(command with { DueDate = null }, CancellationToken.None)).Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        (await Header().HandleAsync(command with { Id = new BillId(Guid.NewGuid()) }, CancellationToken.None)).Error.Code.ShouldBe(BillingErrors.BillNotFoundCode);
        (await Header().HandleAsync(command with { ExpectedVersion = Text(OlderVersion), BillDate = TestDoubles.Today.AddDays(1) }, CancellationToken.None))
            .Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        (await Header().HandleAsync(command with { BillDate = TestDoubles.Today.AddDays(1), DueDate = TestDoubles.Today.AddDays(1) }, CancellationToken.None))
            .Error.Code.ShouldBe(BillingErrors.BillDateInFutureCode);
        (await Header().HandleAsync(command with { DueDate = TestDoubles.Today.AddDays(-1) }, CancellationToken.None))
            .Error.Code.ShouldBe(BillingErrors.BillDueDateBeforeBillDateCode);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task US23_voiding_a_draft_records_the_reason_the_clock_and_the_user_and_voiding_it_again_is_refused()
    {
        var bill = Existing(CurrentVersion);
        var handler = new VoidBillHandler(new VoidBillCommandValidator(), _clock, _currentUser, _repository, _unitOfWork);

        var voided = await handler.HandleAsync(new VoidBillCommand(bill.Id, Text(CurrentVersion), "Entered twice"), CancellationToken.None);

        voided.Value.Status.ShouldBe(BillStatus.Voided);
        voided.Value.VoidReason.ShouldBe("Entered twice");
        voided.Value.VoidedAtUtc.ShouldBe(_clock.UtcNow);
        bill.VoidedByUserId.ShouldBe(Clerk);
        (await handler.HandleAsync(new VoidBillCommand(bill.Id, Text(CurrentVersion), "Again"), CancellationToken.None)).Error.Code.ShouldBe(BillingErrors.BillNotDraftCode);
        (await handler.HandleAsync(new VoidBillCommand(bill.Id, Text(OlderVersion), "Stale"), CancellationToken.None)).Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        (await handler.HandleAsync(new VoidBillCommand(bill.Id, Text(CurrentVersion), " "), CancellationToken.None)).Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        (await handler.HandleAsync(new VoidBillCommand(new BillId(Guid.NewGuid()), Text(CurrentVersion), "Unknown"), CancellationToken.None)).Error.Code.ShouldBe(BillingErrors.BillNotFoundCode);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reading_and_listing_bills()
    {
        var known = Existing(CurrentVersion);
        var dto = known.ToDto();
        _bills.GetAsync(known.Id, Arg.Any<CancellationToken>()).Returns(dto);
        var page = new PageOf<BillSummaryDto>([known.ToSummary()], 1, 20, 1);
        _bills.ListAsync(Arg.Any<ListBillsQuery>(), Arg.Any<CancellationToken>()).Returns(page);

        (await new GetBillHandler(_bills).HandleAsync(new GetBillQuery(known.Id), CancellationToken.None)).Value.ShouldBeSameAs(dto);
        (await new GetBillHandler(_bills).HandleAsync(new GetBillQuery(new BillId(Guid.NewGuid())), CancellationToken.None)).Error.Code.ShouldBe(BillingErrors.BillNotFoundCode);
        (await new ListBillsHandler(new ListBillsQueryValidator(), _bills).HandleAsync(new ListBillsQuery(), CancellationToken.None)).Value.ShouldBeSameAs(page);
        (await new ListBillsHandler(new ListBillsQueryValidator(), _bills).HandleAsync(new ListBillsQuery { Page = 0 }, CancellationToken.None)).Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
    }

    private CreateBillDraftHandler Create() => new(new CreateBillDraftCommandValidator(), _parties, _settings, _items, _clock, _repository, _unitOfWork);

    private ReplaceBillLinesHandler Replace() => new(new ReplaceBillLinesCommandValidator(), _items, _repository, _unitOfWork);

    private UpdateBillHeaderHandler Header() => new(new UpdateBillHeaderCommandValidator(), _settings, _clock, _repository, _unitOfWork);

    // A service item as the catalogue would return it.
    private ServiceItemId InCatalogue(string name, decimal price, decimal taxRate, ServiceItemStatus status = ServiceItemStatus.Active)
        => InCatalogue(new ServiceItemId(Guid.NewGuid()), name, price, taxRate, status);

    private ServiceItemId InCatalogue(ServiceItemId id, string name, decimal price, decimal taxRate, ServiceItemStatus status)
    {
        var item = new ServiceItemDto(id.Value, "ITEM-1", name, null, price, taxRate, status, _clock.UtcNow, _clock.UtcNow, Text(CurrentVersion));
        _items.GetManyAsync(Arg.Is<IReadOnlyCollection<ServiceItemId>>(ids => ids.Contains(id)), Arg.Any<CancellationToken>()).Returns([item]);

        return id;
    }

    // A draft as the repository would load it: saved, with an id and a row version, and one free-text line.
    private Bill Existing(byte[] version) => ExistingWith(version, new BillLineDetails
    {
        Description = "Dressing",
        Quantity = Quantity.Of(2m),
        UnitPrice = UnitPrice.Of(60m),
        TaxRate = Percent.Of(5m),
    });

    private Bill ExistingWith(byte[] version, BillLineDetails line)
    {
        var bill = Bill.Create(
            Patient,
            Payer,
            new BillHeader { BillDate = TestDoubles.Today, DueDate = TestDoubles.Today },
            [line],
            new BusinessDateWindow(TestDoubles.Today, 7)).Value.Saved(version);
        var id = new BillId(Guid.NewGuid());
        typeof(Entity<BillId>).GetProperty(nameof(Entity<BillId>.Id))!.SetValue(bill, id);
        _repository.GetAsync(id, Arg.Any<CancellationToken>()).Returns(bill);

        return bill;
    }

    private static string Text(byte[] version) => Convert.ToBase64String(version);

    private static CreateBillDraftCommand NewDraft(params BillLineInput[] lines) => new() { PatientId = Patient, PayerId = Payer, Lines = lines };

    private static ReplaceBillLinesCommand Lines(BillId id, byte[] version, params BillLineInput[] lines)
        => new() { Id = id, ExpectedVersion = Text(version), Lines = lines };

    // 2 x 60.00 at 5%: 120.00 + 6.00 = 126.00.
    private static BillLineInput FreeText() => new() { Description = "Dressing", Quantity = 2m, UnitPrice = 60m, TaxRatePercent = 5m };
}
