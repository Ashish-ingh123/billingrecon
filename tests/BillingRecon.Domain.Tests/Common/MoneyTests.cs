using System.Globalization;
using System.Reflection;
using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Common;

/// <summary>
/// Money (BR-001, ADR 0004): exact to the paisa, rounded once and half away from zero, summed without rounding, and
/// without the operators that would hide a rounding point.
/// </summary>
public sealed class MoneyTests
{
    [Theory]
    [InlineData("2.345", "2.35")]
    [InlineData("-2.345", "-2.35")]
    [InlineData("49.975", "49.98")]
    [InlineData("49.985", "49.99")]
    [InlineData("-0.005", "-0.01")]
    [InlineData("0.005", "0.01")]
    [InlineData("1.004", "1.00")]
    [InlineData("1.0049999", "1.00")]
    [InlineData("1.005", "1.01")]
    [InlineData("999.9999", "1000.00")]
    [InlineData("0", "0.00")]
    public void Round_rounds_to_two_decimals_half_away_from_zero(string raw, string expected)
    {
        var money = Money.Round(Decimal(raw));

        money.Amount.ShouldBe(Decimal(expected));
        money.ToString().ShouldBe(expected);
    }

    [Theory]
    [InlineData("2.345", "2.34")]
    [InlineData("49.985", "49.98")]
    [InlineData("-0.005", "0.00")]
    [InlineData("0.125", "0.12")]
    public void Bankers_rounding_would_give_another_answer_which_is_why_the_rule_is_written_down(string raw, string bankers)
    {
        // Math.Round defaults to MidpointRounding.ToEven: a midpoint goes to the even neighbour, so 2.345 becomes 2.34
        // and 2.355 becomes 2.36. A patient with a calculator, a cashier and SQL Server round half away from zero;
        // the ledger follows them, so that what is printed is what is stored.
        Math.Round(Decimal(raw), 2).ShouldBe(Decimal(bankers));
        Money.Round(Decimal(raw)).Amount.ShouldNotBe(Decimal(bankers));
    }

    [Fact]
    public void Round_keeps_every_invariant_over_many_random_values()
    {
        // For any raw value: the result is money, within half a paisa of the raw value, symmetric in sign, and never
        // out of order with its neighbours. A seeded generator, so a failure can be reproduced: values up to seven
        // decimals and a hundred thousand rupees either way.
        var random = new Random(20261017);
        var raws = Enumerable.Range(0, 5000)
            .Select(_ => random.NextInt64(-1_000_000_000_000L, 1_000_000_000_000L) / 10_000_000m)
            .Order()
            .ToArray();

        var previous = Money.Round(raws[0]);
        foreach (var raw in raws)
        {
            var rounded = Money.Round(raw);

            rounded.Amount.Scale.ShouldBe((byte)2);
            Math.Abs(rounded.Amount - raw).ShouldBeLessThanOrEqualTo(Decimal("0.005"));
            Money.Round(-raw).ShouldBe(-rounded);
            (rounded >= previous).ShouldBeTrue($"rounding went backwards at {raw.ToString(CultureInfo.InvariantCulture)}");
            previous = rounded;
        }
    }

    [Fact]
    public void Round_of_an_exact_value_is_that_value()
    {
        Money.Round(Decimal("12.30")).ShouldBe(Money.Of(Decimal("12.30")));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("5")]
    [InlineData("5.5")]
    [InlineData("5.55")]
    [InlineData("-5.55")]
    [InlineData("0.01")]
    [InlineData("1234567890123456.78")]
    public void Of_accepts_a_value_with_no_digit_below_the_paisa(string value)
    {
        Money.Of(Decimal(value)).Amount.ShouldBe(Decimal(value));
    }

    [Fact]
    public void Of_accepts_trailing_zeros_beyond_the_paisa_as_the_same_money()
    {
        // JSON gives 5.000 a scale of three; it is still exactly five rupees.
        var money = Money.Of(Decimal("5.000"));

        money.ShouldBe(Money.Of(5m));
        money.ToString().ShouldBe("5.00");
    }

    [Theory]
    [InlineData("2.345")]
    [InlineData("0.001")]
    [InlineData("-1.0001")]
    [InlineData("5.0000001")]
    public void Of_refuses_a_digit_below_the_paisa_because_an_unrounded_result_is_a_bug(string value)
    {
        var exception = Should.Throw<ArgumentException>(() => Money.Of(Decimal(value)));

        exception.ParamName.ShouldBe("amount");
        exception.Message.ShouldContain("Money.Round");
    }

    [Theory]
    [InlineData("5.000", true)]
    [InlineData("-0.50", true)]
    [InlineData("5.001", false)]
    [InlineData("0.005", false)]
    public void TryOf_says_whether_a_value_is_money_without_throwing(string value, bool expected)
    {
        Money.TryOf(Decimal(value), out var money).ShouldBe(expected);

        if (expected)
        {
            money.Amount.ShouldBe(Decimal(value));
        }
        else
        {
            money.ShouldBe(default);
        }
    }

    [Fact]
    public void Every_amount_the_type_produces_has_exactly_two_decimals()
    {
        // 5 and 5.000 are the same money and print the same; the scale is normalised, not merely tolerated.
        Money.Of(5m).Amount.Scale.ShouldBe((byte)2);
        Money.Of(Decimal("5.5")).Amount.Scale.ShouldBe((byte)2);
        Money.Of(Decimal("5.000")).Amount.Scale.ShouldBe((byte)2);
        Money.Round(5m).Amount.Scale.ShouldBe((byte)2);
        Money.Round(Decimal("2.345")).Amount.Scale.ShouldBe((byte)2);
        (Money.Of(1m) + Money.Of(2m)).Amount.Scale.ShouldBe((byte)2);
        (Money.Of(3m) - Money.Of(1m)).Amount.Scale.ShouldBe((byte)2);
        (-Money.Of(1m)).Amount.Scale.ShouldBe((byte)2);
        new[] { Money.Of(1m), Money.Of(2m) }.Sum().Amount.Scale.ShouldBe((byte)2);
        Money.Zero.Amount.Scale.ShouldBe((byte)2);
    }

    [Fact]
    public void Equal_amounts_are_equal_and_hash_alike_whatever_their_scale()
    {
        var five = Money.Of(5m);
        var fivePointZeroZero = Money.Of(Decimal("5.00"));

        five.ShouldBe(fivePointZeroZero);
        (five == fivePointZeroZero).ShouldBeTrue();
        (five != fivePointZeroZero).ShouldBeFalse();
        five.GetHashCode().ShouldBe(fivePointZeroZero.GetHashCode());
        five.ShouldNotBe(Money.Of(Decimal("5.01")));
    }

    [Fact]
    public void Zero_is_the_default_and_the_sign_predicates_agree()
    {
        default(Money).ShouldBe(Money.Zero);
        Money.Zero.IsZero.ShouldBeTrue();
        Money.Zero.IsNegative.ShouldBeFalse();
        Money.Zero.IsPositive.ShouldBeFalse();

        var credit = Money.Of(Decimal("0.01"));
        credit.IsPositive.ShouldBeTrue();
        credit.IsZero.ShouldBeFalse();
        credit.IsNegative.ShouldBeFalse();

        var debit = Money.Of(Decimal("-0.01"));
        debit.IsNegative.ShouldBeTrue();
        debit.IsZero.ShouldBeFalse();
        debit.IsPositive.ShouldBeFalse();
    }

    [Fact]
    public void Addition_subtraction_and_negation_are_exact_and_need_no_rounding()
    {
        // The classic trap of binary floating point, 0.1 + 0.2 is not 0.3, does not exist for decimal.
        (Money.Of(Decimal("0.10")) + Money.Of(Decimal("0.20"))).ShouldBe(Money.Of(Decimal("0.30")));
        (Money.Of(Decimal("1000.00")) - Money.Of(Decimal("0.01"))).ShouldBe(Money.Of(Decimal("999.99")));
        (Money.Of(Decimal("0.50")) - Money.Of(Decimal("0.75"))).ShouldBe(Money.Of(Decimal("-0.25")));
        (-Money.Of(Decimal("2.35"))).ShouldBe(Money.Of(Decimal("-2.35")));
        (-Money.Zero).ShouldBe(Money.Zero);
    }

    [Fact]
    public void Comparisons_follow_the_amount()
    {
        var less = Money.Of(Decimal("9.99"));
        var more = Money.Of(Decimal("10.00"));
        var same = Money.Of(Decimal("10.00"));

        (less < more).ShouldBeTrue();
        (less <= more).ShouldBeTrue();
        (more > less).ShouldBeTrue();
        (more >= less).ShouldBeTrue();
        (more <= same).ShouldBeTrue();
        (more >= same).ShouldBeTrue();
        (more < same).ShouldBeFalse();
        (more > same).ShouldBeFalse();
        less.CompareTo(more).ShouldBeLessThan(0);
        more.CompareTo(less).ShouldBeGreaterThan(0);
        more.CompareTo(same).ShouldBe(0);
        new[] { more, less, same }.Order().ShouldBe([less, more, same]);
    }

    [Fact]
    public void A_bill_of_a_thousand_lines_totals_the_sum_of_its_rounded_lines_in_any_order()
    {
        // Line amounts the way BR-202 computes them: quantity × unit price, rounded once. A seeded generator, so the
        // bill is the same in every run: quantities with one decimal, unit prices with four.
        var random = new Random(20261017);
        var lines = Enumerable.Range(0, 1000)
            .Select(_ => Money.Round(random.Next(1, 100) * 0.5m * (random.Next(1, 100000) / 10000m)))
            .ToArray();

        var total = lines.Sum();

        total.ShouldBe(Enumerable.Reverse(lines).Sum());
        total.ShouldBe(lines.Aggregate(Money.Zero, (sum, line) => sum + line));
        total.ShouldBe(Money.Of(lines.Sum(line => line.Amount)));
        total.IsPositive.ShouldBeTrue();
    }

    [Fact]
    public void Lines_are_rounded_before_they_are_summed_so_the_printed_lines_add_up_to_the_printed_total()
    {
        decimal[] rawLines = [Decimal("0.333"), Decimal("0.333"), Decimal("0.333")];

        var lineLevel = rawLines.Select(Money.Round).Sum();
        var documentLevel = Money.Round(rawLines.Sum());

        // Three printed lines of 0.33 add up to 0.99; a total rounded from the raw sum would print 1.00 under them.
        lineLevel.ShouldBe(Money.Of(Decimal("0.99")));
        documentLevel.ShouldBe(Money.Of(Decimal("1.00")));
    }

    [Fact]
    public void Sum_of_nothing_is_zero_and_sum_refuses_null()
    {
        Enumerable.Empty<Money>().Sum().ShouldBe(Money.Zero);
        Should.Throw<ArgumentNullException>(() => ((IEnumerable<Money>)null!).Sum());
    }

    [Fact]
    public void Money_has_no_multiplication_division_or_conversion_operator()
    {
        // Multiplying money would hide the rounding point; converting from a decimal would skip the check of Of.
        var operators = typeof(Money)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.IsSpecialName && method.Name.StartsWith("op_", StringComparison.Ordinal))
            .Select(method => method.Name)
            .ToArray();

        operators.ShouldContain("op_Addition");
        operators.ShouldContain("op_Subtraction");
        operators.ShouldContain("op_UnaryNegation");
        operators.ShouldContain("op_LessThan");
        operators.ShouldContain("op_GreaterThan");
        operators.ShouldNotContain("op_Multiply");
        operators.ShouldNotContain("op_Division");
        operators.ShouldNotContain("op_Implicit");
        operators.ShouldNotContain("op_Explicit");
    }

    private static decimal Decimal(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);
}
