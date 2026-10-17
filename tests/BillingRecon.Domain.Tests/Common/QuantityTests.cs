using System.Globalization;
using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Common;

/// <summary>Quantities (BR-201, ADR 0004): above zero, exact to three decimals.</summary>
public sealed class QuantityTests
{
    [Theory]
    [InlineData("0.001")]
    [InlineData("3")]
    [InlineData("2.5")]
    [InlineData("1.000")]
    [InlineData("999999.999")]
    public void Of_accepts_a_positive_value_with_no_digit_below_the_third_decimal(string value)
    {
        Quantity.Of(Decimal(value)).Value.ShouldBe(Decimal(value));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-0.001")]
    public void Of_refuses_zero_and_negative_quantities(string value)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Quantity.Of(Decimal(value))).ParamName.ShouldBe("value");
    }

    [Theory]
    [InlineData("1.0001")]
    [InlineData("0.0005")]
    public void Of_refuses_a_digit_below_the_third_decimal(string value)
    {
        Should.Throw<ArgumentException>(() => Quantity.Of(Decimal(value))).ParamName.ShouldBe("value");
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0.500", true)]
    [InlineData("0.5000", true)]
    [InlineData("0", false)]
    [InlineData("-2", false)]
    [InlineData("1.0001", false)]
    public void TryOf_says_whether_a_value_is_a_quantity_without_throwing(string value, bool expected)
    {
        Quantity.TryOf(Decimal(value), out var quantity).ShouldBe(expected);

        if (expected)
        {
            quantity.Value.ShouldBe(Decimal(value));
        }
        else
        {
            quantity.ShouldBe(default);
        }
    }

    [Fact]
    public void The_default_of_the_struct_is_not_a_valid_quantity()
    {
        // A struct always has a default; this one is zero, which no code path produces as a quantity.
        default(Quantity).Value.ShouldBe(0m);
        Quantity.TryOf(default(Quantity).Value, out _).ShouldBeFalse();
    }

    [Fact]
    public void Comparisons_follow_the_value()
    {
        var one = Quantity.Of(1m);
        var two = Quantity.Of(2m);
        var alsoTwo = Quantity.Of(Decimal("2.000"));

        (one < two).ShouldBeTrue();
        (one <= two).ShouldBeTrue();
        (two > one).ShouldBeTrue();
        (two >= one).ShouldBeTrue();
        (two < alsoTwo).ShouldBeFalse();
        (two > alsoTwo).ShouldBeFalse();
        (two <= alsoTwo).ShouldBeTrue();
        (two >= alsoTwo).ShouldBeTrue();
        two.ShouldBe(alsoTwo);
        one.CompareTo(two).ShouldBeLessThan(0);
        two.CompareTo(alsoTwo).ShouldBe(0);
    }

    private static decimal Decimal(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);
}
