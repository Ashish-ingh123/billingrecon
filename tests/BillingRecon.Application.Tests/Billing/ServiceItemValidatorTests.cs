using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Tests.Billing;

/// <summary>The shape of the service item commands and queries (BR-201). That a code is free in the tenant is not a shape.</summary>
public sealed class ServiceItemValidatorTests
{
    private readonly CreateServiceItemCommandValidator _create = new();
    private readonly UpdateServiceItemCommandValidator _update = new();
    private readonly ListServiceItemsQueryValidator _list = new();

    [Fact]
    public async Task A_complete_item_and_one_without_a_category_are_both_valid()
    {
        (await _create.GetErrorAsync(Create(), CancellationToken.None)).ShouldBeNull();
        (await _create.GetErrorAsync(Create() with { Category = null }, CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CONS OPD")]
    [InlineData("CONS#1")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ01234")]
    public async Task A_code_that_is_blank_has_other_characters_or_is_too_long_is_refused(string code)
    {
        var error = await _create.GetErrorAsync(Create() with { Code = code }, CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["code"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_item_without_a_name_is_refused(string name)
    {
        var error = await _create.GetErrorAsync(Create() with { Name = name }, CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["name"]);
    }

    [Fact]
    public async Task A_name_and_a_category_longer_than_their_columns_are_refused()
    {
        var error = await _create.GetErrorAsync(
            Create() with { Name = new string('n', ServiceItem.NameMaxLength + 1), Category = new string('c', ServiceItem.CategoryMaxLength + 1) },
            CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["name", "category"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_price_and_a_tax_rate_are_required_rather_than_defaulted_to_zero()
    {
        var error = await _create.GetErrorAsync(Create() with { DefaultUnitPrice = null, TaxRatePercent = null }, CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["defaultUnitPrice", "taxRatePercent"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.0001")]
    [InlineData("333.3333")]
    [InlineData("999999999999999.9999")]
    public async Task BR201_a_price_of_zero_or_more_with_up_to_four_decimals_is_valid(string price)
    {
        (await _create.GetErrorAsync(Create() with { DefaultUnitPrice = Number(price) }, CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1.00001")]
    [InlineData("1000000000000000")]
    public async Task BR201_a_negative_price_one_with_five_decimals_and_one_the_column_cannot_hold_are_refused(string price)
    {
        var error = await _create.GetErrorAsync(Create() with { DefaultUnitPrice = Number(price) }, CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["defaultUnitPrice"]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("2.5")]
    [InlineData("18")]
    [InlineData("100")]
    public async Task BR201_a_tax_rate_from_0_to_100_with_up_to_two_decimals_is_valid(string taxRate)
    {
        (await _create.GetErrorAsync(Create() with { TaxRatePercent = Number(taxRate) }, CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("100.01")]
    [InlineData("12.345")]
    public async Task BR201_a_tax_rate_outside_0_to_100_or_with_three_decimals_is_refused(string taxRate)
    {
        var error = await _create.GetErrorAsync(Create() with { TaxRatePercent = Number(taxRate) }, CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["taxRatePercent"]);
    }

    [Fact]
    public async Task A_correction_needs_the_version_the_client_read_and_the_same_details()
    {
        var update = new UpdateServiceItemCommand
        {
            Id = new ServiceItemId(Guid.NewGuid()),
            ExpectedVersion = "AAAAAAAAB9E=",
            Name = "OPD consultation",
            DefaultUnitPrice = 500m,
            TaxRatePercent = 18m,
        };

        (await _update.GetErrorAsync(update, CancellationToken.None)).ShouldBeNull();

        var error = await _update.GetErrorAsync(
            update with { ExpectedVersion = string.Empty, Name = string.Empty, DefaultUnitPrice = -1m, TaxRatePercent = 101m },
            CancellationToken.None);
        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["expectedVersion", "name", "defaultUnitPrice", "taxRatePercent"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_list_query_checks_the_page_the_search_and_the_status()
    {
        (await _list.GetErrorAsync(new ListServiceItemsQuery(), CancellationToken.None)).ShouldBeNull();

        var error = await _list.GetErrorAsync(
            new ListServiceItemsQuery
            {
                Page = 0,
                PageSize = Paging.MaxPageSize + 1,
                Search = new string('s', ListServiceItemsQuery.SearchMaxLength + 1),
                Status = (ServiceItemStatus)7,
            },
            CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["page", "pageSize", "search", "status"], ignoreOrder: true);
    }

    // A decimal cannot be an attribute argument, so the theories carry their numbers as text.
    private static decimal Number(string text) => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    private static CreateServiceItemCommand Create() => new()
    {
        Code = "CONS-OPD",
        Name = "OPD consultation",
        Category = "Consultation",
        DefaultUnitPrice = 500m,
        TaxRatePercent = 18m,
    };
}
