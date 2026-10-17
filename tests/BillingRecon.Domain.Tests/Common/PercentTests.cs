using System.Globalization;
using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Common;

/// <summary>Percentages (BR-201, ADR 0004): 0 to 100, exact to two decimals; the tax rate of a line.</summary>
public sealed class PercentTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("18")]
    [InlineData("100")]
    [InlineData("12.5")]
    [InlineData("5.00")]
    [InlineData("99.99")]
    public void Of_accepts_a_value_between_zero_and_a_hundred_with_no_digit_below_the_second_decimal(string value)
    {
        Percent.Of(Decimal(value)).Value.ShouldBe(Decimal(value));
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("100.01")]
    [InlineData("250")]
    public void Of_refuses_a_value_outside_zero_to_a_hundred(string value)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Percent.Of(Decimal(value))).ParamName.ShouldBe("value");
    }

    [Theory]
    [InlineData("12.345")]
    [InlineData("0.001")]
    public void Of_refuses_a_digit_below_the_second_decimal(string value)
    {
        Should.Throw<ArgumentException>(() => Percent.Of(Decimal(value))).ParamName.ShouldBe("value");
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("18.00", true)]
    [InlineData("18.000", true)]
    [InlineData("100", true)]
    [InlineData("100.5", false)]
    [InlineData("-1", false)]
    [InlineData("5.005", false)]
    public void TryOf_says_whether_a_value_is_a_percentage_without_throwing(string value, bool expected)
    {
        Percent.TryOf(Decimal(value), out var percent).ShouldBe(expected);

        if (expected)
        {
            percent.Value.ShouldBe(Decimal(value));
        }
        else
        {
            percent.ShouldBe(default);
        }
    }

    [Fact]
    public void Zero_is_the_default_and_comparisons_follow_the_value()
    {
        default(Percent).ShouldBe(Percent.Zero);

        var low = Percent.Of(5m);
        var high = Percent.Of(18m);
        var alsoHigh = Percent.Of(Decimal("18.00"));

        (low < high).ShouldBeTrue();
        (low <= high).ShouldBeTrue();
        (high > low).ShouldBeTrue();
        (high >= low).ShouldBeTrue();
        (high < alsoHigh).ShouldBeFalse();
        (high > alsoHigh).ShouldBeFalse();
        (high <= alsoHigh).ShouldBeTrue();
        (high >= alsoHigh).ShouldBeTrue();
        high.ShouldBe(alsoHigh);
        low.CompareTo(high).ShouldBeLessThan(0);
        high.CompareTo(alsoHigh).ShouldBe(0);
    }

    [Fact]
    public void A_tax_amount_is_the_taxable_amount_times_the_rate_rounded_once()
    {
        // The BR-202 example of docs/domain-model.md: 999.50 at 5 per cent is 49.975, which rounds to 49.98 away from
        // zero (banker's rounding would give 49.98 here too, but 49.985 would become 49.98 instead of 49.99).
        var taxable = Money.Of(Decimal("999.50"));
        var rate = Percent.Of(5m);

        Money.Round(taxable.Amount * rate.Value / 100m).ShouldBe(Money.Of(Decimal("49.98")));
    }

    private static decimal Decimal(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);
}
