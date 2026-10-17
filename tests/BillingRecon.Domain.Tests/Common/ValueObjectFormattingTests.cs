using System.Globalization;
using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Common;

/// <summary>Every value object prints its fixed number of decimals with a dot and no grouping, whatever the current culture.</summary>
public sealed class ValueObjectFormattingTests
{
    [Theory]
    [InlineData("")] // the invariant culture
    [InlineData("en-IN")] // lakh grouping
    [InlineData("de-DE")] // decimal comma, grouping dot
    public void Every_value_object_prints_its_fixed_decimals_with_a_dot_and_no_grouping(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

            Money.Of(Decimal("1234567.5")).ToString().ShouldBe("1234567.50");
            Money.Of(Decimal("-0.05")).ToString().ShouldBe("-0.05");
            Money.Zero.ToString().ShouldBe("0.00");
            UnitPrice.Of(Decimal("333.3333")).ToString().ShouldBe("333.3333");
            UnitPrice.Of(Decimal("25.5")).ToString().ShouldBe("25.5000");
            UnitPrice.Zero.ToString().ShouldBe("0.0000");
            Quantity.Of(Decimal("2.5")).ToString().ShouldBe("2.500");
            Quantity.Of(1000m).ToString().ShouldBe("1000.000");
            Percent.Of(18m).ToString().ShouldBe("18.00");
            Percent.Of(Decimal("12.5")).ToString().ShouldBe("12.50");
            Percent.Zero.ToString().ShouldBe("0.00");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static decimal Decimal(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);
}
