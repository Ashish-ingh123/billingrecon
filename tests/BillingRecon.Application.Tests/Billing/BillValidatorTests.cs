using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Tests.Billing;

/// <summary>
/// The shape of the bill commands and queries (BR-201). What depends on the catalogue, the clock or the state of the
/// bill is not a shape: those rules answer 404 or 422 after this.
/// </summary>
public sealed class BillValidatorTests
{
    private static readonly ServiceItemId Item = new(Guid.NewGuid());

    private readonly CreateBillDraftCommandValidator _create = new();
    private readonly ReplaceBillLinesCommandValidator _replace = new();
    private readonly UpdateBillHeaderCommandValidator _header = new();
    private readonly VoidBillCommandValidator _void = new();
    private readonly ListBillsQueryValidator _list = new();

    [Fact]
    public async Task A_draft_without_lines_and_one_with_a_catalogue_line_and_a_free_text_line_are_valid()
    {
        (await _create.GetErrorAsync(Create(), CancellationToken.None)).ShouldBeNull();
        (await _create.GetErrorAsync(Create(FromCatalogue(), FreeText()), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task A_draft_needs_a_patient_and_a_payer()
    {
        var error = await _create.GetErrorAsync(
            Create() with { PatientId = new PatientId(Guid.Empty), PayerId = new PayerId(Guid.Empty) },
            CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["patientId", "payerId"], ignoreOrder: true);
    }

    [Fact]
    public async Task The_texts_of_the_header_have_a_bounded_length()
    {
        var error = await _create.GetErrorAsync(
            Create() with { EncounterReference = new string('e', Bill.EncounterReferenceMaxLength + 1), Notes = new string('n', Bill.NotesMaxLength + 1) },
            CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["encounterReference", "notes"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_line_from_the_catalogue_needs_only_a_quantity()
    {
        var line = new BillLineInput { ServiceItemId = Item, Quantity = 1m };

        (await _create.GetErrorAsync(Create(line), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task BR201_a_free_text_line_needs_its_own_description_unit_price_and_tax_rate()
    {
        var error = await _create.GetErrorAsync(Create(new BillLineInput { Quantity = 1m }), CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["lines[0].description", "lines[0].unitPrice", "lines[0].taxRatePercent"], ignoreOrder: true);
    }

    [Fact]
    public async Task An_error_is_reported_under_the_position_of_its_line()
    {
        var error = await _create.GetErrorAsync(Create(FreeText(), FreeText() with { Quantity = 0m }, FreeText() with { TaxRatePercent = 101m }), CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["lines[1].quantity", "lines[2].taxRatePercent"], ignoreOrder: true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.0001")]
    [InlineData("1000000")]
    public async Task BR201_a_quantity_that_is_missing_not_above_zero_too_fine_or_too_large_is_refused(string? quantity)
    {
        var error = await _create.GetErrorAsync(Create(FreeText() with { Quantity = Number(quantity) }), CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["lines[0].quantity"]);
    }

    [Theory]
    [InlineData("0.001")]
    [InlineData("0.333")]
    [InlineData("999999.999")]
    public async Task BR201_a_quantity_above_zero_with_up_to_three_decimals_is_valid(string quantity)
    {
        (await _create.GetErrorAsync(Create(FreeText() with { Quantity = Number(quantity) }), CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData("-0.01", "lines[0].unitPrice")]
    [InlineData("1.00001", "lines[0].unitPrice")]
    public async Task BR201_a_unit_price_that_is_negative_or_too_fine_is_refused(string unitPrice, string field)
    {
        var error = await _create.GetErrorAsync(Create(FreeText() with { UnitPrice = Number(unitPrice) }), CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe([field]);
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("0.005")]
    public async Task BR201_a_discount_that_is_negative_or_finer_than_the_paisa_is_refused(string discount)
    {
        var error = await _create.GetErrorAsync(Create(FreeText() with { DiscountAmount = Number(discount) }), CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["lines[0].discountAmount"]);
    }

    [Fact]
    public async Task BR201_a_discount_larger_than_the_line_passes_the_shape_check_and_is_left_to_the_business_rule()
    {
        // The line amount depends on the price, which may come from the catalogue; the aggregate answers with 422.
        (await _create.GetErrorAsync(Create(FreeText() with { DiscountAmount = 9999m }), CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("100.01")]
    [InlineData("12.345")]
    public async Task BR201_a_tax_rate_outside_0_to_100_or_with_three_decimals_is_refused(string taxRate)
    {
        var error = await _create.GetErrorAsync(Create(FreeText() with { TaxRatePercent = Number(taxRate) }), CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["lines[0].taxRatePercent"]);
    }

    [Fact]
    public async Task A_description_longer_than_its_column_and_an_empty_service_item_id_are_refused()
    {
        var error = await _create.GetErrorAsync(
            Create(FreeText() with { Description = new string('d', BillLine.DescriptionMaxLength + 1), ServiceItemId = new ServiceItemId(Guid.Empty) }),
            CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["lines[0].description", "lines[0].serviceItemId"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_bill_has_at_most_two_hundred_lines()
    {
        var atTheLimit = Enumerable.Repeat(FreeText(), Bill.MaxLines).ToArray();
        var oneTooMany = Enumerable.Repeat(FreeText(), Bill.MaxLines + 1).ToArray();

        (await _create.GetErrorAsync(Create(atTheLimit), CancellationToken.None)).ShouldBeNull();
        (await _create.GetErrorAsync(Create(oneTooMany), CancellationToken.None)).ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["lines"]);
    }

    [Fact]
    public async Task Replacing_lines_needs_the_version_the_client_read_and_checks_each_line()
    {
        var replace = new ReplaceBillLinesCommand { Id = new BillId(Guid.NewGuid()), ExpectedVersion = "AAAAAAAAB9E=", Lines = [FreeText()] };

        (await _replace.GetErrorAsync(replace, CancellationToken.None)).ShouldBeNull();
        (await _replace.GetErrorAsync(replace with { Lines = [] }, CancellationToken.None)).ShouldBeNull();

        var error = await _replace.GetErrorAsync(replace with { ExpectedVersion = string.Empty, Lines = [FreeText() with { Quantity = null }] }, CancellationToken.None);
        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["expectedVersion", "lines[0].quantity"], ignoreOrder: true);
    }

    [Fact]
    public async Task Correcting_the_header_needs_the_version_and_both_dates()
    {
        var header = new UpdateBillHeaderCommand
        {
            Id = new BillId(Guid.NewGuid()),
            ExpectedVersion = "AAAAAAAAB9E=",
            BillDate = new DateOnly(2026, 11, 20),
            DueDate = new DateOnly(2026, 12, 5),
        };

        (await _header.GetErrorAsync(header, CancellationToken.None)).ShouldBeNull();

        var error = await _header.GetErrorAsync(
            header with { ExpectedVersion = string.Empty, BillDate = null, DueDate = null, Notes = new string('n', Bill.NotesMaxLength + 1) },
            CancellationToken.None);
        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["expectedVersion", "billDate", "dueDate", "notes"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task US23_voiding_needs_a_reason(string reason)
    {
        var id = new BillId(Guid.NewGuid());

        (await _void.GetErrorAsync(new VoidBillCommand(id, "AAAAAAAAB9E=", "Entered twice"), CancellationToken.None)).ShouldBeNull();
        (await _void.GetErrorAsync(new VoidBillCommand(id, "AAAAAAAAB9E=", reason), CancellationToken.None)).ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["reason"]);
        (await _void.GetErrorAsync(new VoidBillCommand(id, "AAAAAAAAB9E=", new string('r', Bill.VoidReasonMaxLength + 1)), CancellationToken.None))
            .ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["reason"]);
    }

    [Fact]
    public async Task A_list_query_checks_the_page_the_ids_the_status_and_the_date_range()
    {
        (await _list.GetErrorAsync(new ListBillsQuery(), CancellationToken.None)).ShouldBeNull();
        (await _list.GetErrorAsync(
            new ListBillsQuery { BillDateFrom = new DateOnly(2026, 11, 1), BillDateTo = new DateOnly(2026, 11, 1), Status = BillStatus.Voided },
            CancellationToken.None)).ShouldBeNull();

        var error = await _list.GetErrorAsync(
            new ListBillsQuery
            {
                Page = 0,
                PageSize = Paging.MaxPageSize + 1,
                PatientId = new PatientId(Guid.Empty),
                PayerId = new PayerId(Guid.Empty),
                Status = (BillStatus)7,
                BillDateFrom = new DateOnly(2026, 11, 2),
                BillDateTo = new DateOnly(2026, 11, 1),
            },
            CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["page", "pageSize", "patientId", "payerId", "status", "billDateTo"], ignoreOrder: true);
    }

    // A decimal cannot be an attribute argument, so the theories carry their numbers as text.
    private static decimal? Number(string? text) => text is null ? null : decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    private static CreateBillDraftCommand Create(params BillLineInput[] lines) => new()
    {
        PatientId = new PatientId(Guid.NewGuid()),
        PayerId = new PayerId(Guid.NewGuid()),
        Lines = lines,
    };

    private static BillLineInput FromCatalogue() => new() { ServiceItemId = Item, Quantity = 2m, DiscountAmount = 10m };

    private static BillLineInput FreeText() => new() { Description = "Dressing", Quantity = 2m, UnitPrice = 60m, TaxRatePercent = 5m };
}
