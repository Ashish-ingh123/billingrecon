using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Tests.Common;

/// <summary>The rules of the text of the one search box of a list of documents (Prompt 11.1; ADR 0036, amended).</summary>
public sealed class DocumentSearchTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("d", null)]
    [InlineData(" d ", null)]
    [InlineData("de", "de")]
    [InlineData("  Kavita Desh ", "Kavita Desh")]
    [InlineData("%%", "%%")]
    public void A_text_is_searched_for_trimmed_and_from_two_characters(string? search, string? term)
        => DocumentSearch.Term(search).ShouldBe(term);

    [Theory]
    [InlineData("590", "590.00")]
    [InlineData("590.0", "590.00")]
    [InlineData("590.00", "590.00")]
    [InlineData("1,250.50", "1250.50")]
    [InlineData("1,00,000", "100000.00")]
    [InlineData("00", "0.00")]
    public void A_text_of_digits_states_an_amount_to_the_paisa(string term, string amount)
        => DocumentSearch.Amount(term).ShouldBe(Money.Of(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("12.345")]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData("5e2")]
    [InlineData("REF 12")]
    [InlineData("12 34")]
    [InlineData("1.2.3")]
    [InlineData("..")]
    [InlineData(",,")]
    [InlineData("000001/26")]
    public void A_text_that_is_not_an_amount_to_the_paisa_states_none(string term)
        => DocumentSearch.Amount(term).ShouldBeNull();
}
