using System.Globalization;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Billing;

/// <summary>
/// The arithmetic of a line (BR-202), with the values that go wrong when rounding is left to chance: midpoints at the
/// half paisa, thirds, a discount of the whole line, tax of nothing and of everything. Then the same rules checked
/// over thousands of generated lines, from a fixed seed so that a failure can be repeated.
/// </summary>
public sealed class LineCalculatorTests
{
    private const int Seed = 20261120;

    [Fact]
    public void BR202_the_documented_example_three_units_at_333_3333_less_half_a_rupee_at_five_percent()
    {
        var amounts = Calculate("3", "333.3333", "0.50", "5");

        amounts.LineAmount.ShouldBe(Money.Of(1000.00m), "999.9999 rounds up to the paisa");
        amounts.TaxableAmount.ShouldBe(Money.Of(999.50m));
        amounts.TaxAmount.ShouldBe(Money.Of(49.98m), "49.975 rounds away from zero, not to the even 49.97");
        amounts.LineTotal.ShouldBe(Money.Of(1049.48m));
    }

    [Theory]
    [InlineData("1", "0.005", "0.01")] // the half paisa goes up
    [InlineData("1", "0.0049", "0.00")] // just below it goes down
    [InlineData("1", "0.015", "0.02")] // banker's rounding would agree here
    [InlineData("1", "0.025", "0.03")] // and would say 0.02 here
    [InlineData("1", "2.675", "2.68")] // exact in decimal; a double would say 2.67
    [InlineData("0.333", "3", "1.00")] // a third of three: 0.999
    [InlineData("0.333", "100", "33.30")]
    [InlineData("0.333", "0.01", "0.00")] // 0.00333
    [InlineData("0.001", "5", "0.01")] // 0.005
    [InlineData("0.001", "4.9999", "0.00")] // 0.0049999
    [InlineData("2.5", "0.005", "0.01")] // 0.0125
    [InlineData("3", "0.3333", "1.00")] // 0.9999
    [InlineData("7", "0", "0.00")] // a free line
    [InlineData("999999.999", "0.0001", "100.00")] // 99.9999999
    public void BR202_the_line_amount_is_the_quantity_times_the_unit_price_rounded_away_from_zero(string quantity, string unitPrice, string expected)
    {
        LineCalculator.LineAmount(Quantity.Of(Number(quantity)), UnitPrice.Of(Number(unitPrice))).ShouldBe(Money.Of(Number(expected)));
    }

    [Theory]
    [InlineData("0.10", "5", "0.01")] // 0.005
    [InlineData("10.25", "2", "0.21")] // 0.205: banker's rounding would say 0.20
    [InlineData("10.35", "2", "0.21")] // 0.207
    [InlineData("0.50", "1", "0.01")] // 0.005
    [InlineData("0.49", "1", "0.00")] // 0.0049
    [InlineData("12.34", "100", "12.34")]
    [InlineData("12.34", "0", "0.00")]
    [InlineData("0.00", "18", "0.00")]
    [InlineData("100.00", "0.01", "0.01")]
    [InlineData("49.99", "0.01", "0.00")] // 0.004999
    [InlineData("1000.00", "12.5", "125.00")]
    [InlineData("33.33", "33.33", "11.11")] // 11.108889
    public void BR202_the_tax_is_the_taxable_amount_at_the_rate_rounded_away_from_zero(string taxable, string rate, string expected)
    {
        LineCalculator.TaxAmount(Money.Of(Number(taxable)), Percent.Of(Number(rate))).ShouldBe(Money.Of(Number(expected)));
    }

    [Fact]
    public void BR202_a_discount_of_the_whole_line_leaves_nothing_to_tax_and_nothing_to_pay()
    {
        var amounts = Calculate("2", "450.25", "900.50", "18");

        amounts.LineAmount.ShouldBe(Money.Of(900.50m));
        amounts.TaxableAmount.ShouldBe(Money.Zero);
        amounts.TaxAmount.ShouldBe(Money.Zero);
        amounts.LineTotal.ShouldBe(Money.Zero);
    }

    [Fact]
    public void BR202_tax_at_one_hundred_percent_doubles_the_taxable_amount()
    {
        var amounts = Calculate("1", "12.34", "0.34", "100");

        amounts.TaxableAmount.ShouldBe(Money.Of(12.00m));
        amounts.TaxAmount.ShouldBe(Money.Of(12.00m));
        amounts.LineTotal.ShouldBe(Money.Of(24.00m));
    }

    [Fact]
    public void BR202_the_tax_is_on_the_amount_after_the_discount_not_before_it()
    {
        // 100.00 at 18% would be 18.00; after a discount of 0.03 it is 99.97 at 18% = 17.9946, which is 17.99.
        Calculate("1", "100", "0.03", "18").TaxAmount.ShouldBe(Money.Of(17.99m));
    }

    [Fact]
    public void BR202_every_generated_line_is_rounded_once_per_product_and_adds_up_exactly()
    {
        var random = new Random(Seed);

        for (var run = 0; run < 5000; run++)
        {
            var quantity = Quantity.Of(random.Next(1, 1_000_000) / 1000m);
            var unitPrice = UnitPrice.Of(random.Next(0, 100_000_000) / 10000m);
            var taxRate = Percent.Of(random.Next(0, 10_001) / 100m);
            var lineAmount = Math.Round(quantity.Value * unitPrice.Value, 2, MidpointRounding.AwayFromZero);
            var discount = Money.Round(lineAmount * random.Next(0, 101) / 100m);
            var because = $"run {run}: {quantity} x {unitPrice}, discount {discount}, tax {taxRate}%";

            var amounts = LineCalculator.Calculate(quantity, unitPrice, discount, taxRate);

            amounts.LineAmount.Amount.ShouldBe(lineAmount, because);
            amounts.DiscountAmount.ShouldBe(discount, because);
            amounts.TaxableAmount.Amount.ShouldBe(lineAmount - discount.Amount, because);
            amounts.TaxAmount.Amount.ShouldBe(Math.Round(amounts.TaxableAmount.Amount * taxRate.Value / 100m, 2, MidpointRounding.AwayFromZero), because);
            amounts.LineTotal.Amount.ShouldBe(amounts.TaxableAmount.Amount + amounts.TaxAmount.Amount, because);

            // What follows from the rules: nothing is negative, the tax never exceeds what it is charged on, and every
            // amount is exact to the paisa.
            amounts.TaxableAmount.IsNegative.ShouldBeFalse(because);
            amounts.TaxAmount.IsNegative.ShouldBeFalse(because);
            (amounts.TaxAmount <= amounts.TaxableAmount).ShouldBeTrue(because);
            foreach (var amount in new[] { amounts.LineAmount, amounts.TaxableAmount, amounts.TaxAmount, amounts.LineTotal })
            {
                decimal.Round(amount.Amount, 2).ShouldBe(amount.Amount, because);
            }
        }
    }

    [Fact]
    public void BR202_rounding_each_line_is_not_the_same_as_rounding_the_sum_and_the_lines_win()
    {
        // Three lines of 0.005 each: 0.01 + 0.01 + 0.01 = 0.03. Rounding the sum, 0.015, would give 0.02.
        var perLine = Enumerable.Repeat(LineCalculator.LineAmount(Quantity.Of(1m), UnitPrice.Of(0.005m)), 3).Sum();

        perLine.ShouldBe(Money.Of(0.03m));
        Money.Round(3 * 0.005m).ShouldBe(Money.Of(0.02m));
    }

    private static LineAmounts Calculate(string quantity, string unitPrice, string discount, string taxRate)
        => LineCalculator.Calculate(Quantity.Of(Number(quantity)), UnitPrice.Of(Number(unitPrice)), Money.Of(Number(discount)), Percent.Of(Number(taxRate)));

    // A decimal cannot be an attribute argument, so the theories carry their numbers as text.
    private static decimal Number(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);
}
