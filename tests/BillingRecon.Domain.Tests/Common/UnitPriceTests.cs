using System.Globalization;
using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Common;

/// <summary>Unit prices (BR-201, ADR 0004): not negative, exact to four decimals, multiplied raw and rounded once.</summary>
public sealed class UnitPriceTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("333.3333")]
    [InlineData("0.0001")]
    [InlineData("25.5")]
    [InlineData("1.00000")]
    public void Of_accepts_a_non_negative_value_with_no_digit_below_the_fourth_decimal(string value)
    {
        UnitPrice.Of(Decimal(value)).Value.ShouldBe(Decimal(value));
    }

    [Theory]
    [InlineData("0.00001")]
    [InlineData("1.23456")]
    public void Of_refuses_a_digit_below_the_fourth_decimal(string value)
    {
        Should.Throw<ArgumentException>(() => UnitPrice.Of(Decimal(value))).ParamName.ShouldBe("value");
    }

    [Theory]
    [InlineData("-0.0001")]
    [InlineData("-5")]
    public void Of_refuses_a_negative_price(string value)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => UnitPrice.Of(Decimal(value))).ParamName.ShouldBe("value");
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("12.3456", true)]
    [InlineData("12.34560", true)]
    [InlineData("12.34567", false)]
    [InlineData("-1", false)]
    public void TryOf_says_whether_a_value_is_a_unit_price_without_throwing(string value, bool expected)
    {
        UnitPrice.TryOf(Decimal(value), out var price).ShouldBe(expected);

        if (expected)
        {
            price.Value.ShouldBe(Decimal(value));
        }
        else
        {
            price.ShouldBe(default);
        }
    }

    [Fact]
    public void Zero_is_the_default_and_comparisons_follow_the_value()
    {
        default(UnitPrice).ShouldBe(UnitPrice.Zero);

        var cheap = UnitPrice.Of(Decimal("9.9999"));
        var dear = UnitPrice.Of(10m);
        var same = UnitPrice.Of(Decimal("10.0000"));

        (cheap < dear).ShouldBeTrue();
        (cheap <= dear).ShouldBeTrue();
        (dear > cheap).ShouldBeTrue();
        (dear >= cheap).ShouldBeTrue();
        (dear < same).ShouldBeFalse();
        (dear > same).ShouldBeFalse();
        (dear <= same).ShouldBeTrue();
        (dear >= same).ShouldBeTrue();
        dear.ShouldBe(same);
        cheap.CompareTo(dear).ShouldBeLessThan(0);
        dear.CompareTo(same).ShouldBe(0);
    }

    [Fact]
    public void A_line_amount_is_the_product_of_quantity_and_unit_price_rounded_once()
    {
        // The BR-202 example of docs/domain-model.md: 3 × 333.3333 is 999.9999, which rounds up to 1000.00.
        var quantity = Quantity.Of(3m);
        var unitPrice = UnitPrice.Of(Decimal("333.3333"));

        Money.Round(quantity.Value * unitPrice.Value).ShouldBe(Money.Of(1000m));
    }

    private static decimal Decimal(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);
}
