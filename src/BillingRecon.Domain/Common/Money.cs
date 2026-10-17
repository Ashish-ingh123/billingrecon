using System.Globalization;

namespace BillingRecon.Domain.Common;

/// <summary>
/// An amount of money in Indian rupees, exact to the paisa (BR-001, ADR 0004). <see cref="Of"/> takes a value that
/// already is money and refuses one with digits below the paisa, because that is an arithmetic result nobody rounded:
/// a bug, not a rounding request. <see cref="Round"/> is the one place that turns an arithmetic result into money,
/// half away from zero. There is no multiplication or division: code multiplies the raw decimals and then calls
/// <see cref="Round"/>, so the rounding point is visible at the call site, and forgetting it does not compile (a
/// <c>decimal</c> is not a <c>Money</c>). Rupees are the only currency through R5; a currency code is a matter for a
/// new ADR (ADR 0004).
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    /// <summary>Decimals a money value has: paise.</summary>
    public const int Scale = 2;

    /// <summary>The one rounding rule: half away from zero, what a patient computes by hand and what SQL Server does.</summary>
    public const MidpointRounding Rounding = MidpointRounding.AwayFromZero;

    // Adding 0.00 raises the scale to two (a decimal sum carries the larger scale of its operands) and rounding to
    // two lowers it again when the value had trailing zeros; an exact value is unchanged by it. So every amount this
    // type produces prints and serialises alike: 5 and 5.000 both become 5.00. The default value of the struct is the
    // one amount that does not pass here; it is zero either way.
    private Money(decimal amount) => Amount = decimal.Round(amount + 0.00m, Scale);

    /// <summary>No money at all. Also the default value of the type.</summary>
    public static Money Zero { get; } = new(0m);

    /// <summary>The amount in rupees, with exactly two decimals.</summary>
    public decimal Amount { get; }

    /// <summary>True for exactly zero.</summary>
    public bool IsZero => Amount == 0m;

    /// <summary>True below zero.</summary>
    public bool IsNegative => Amount < 0m;

    /// <summary>True above zero.</summary>
    public bool IsPositive => Amount > 0m;

    /// <summary>
    /// Money from a value that already is money: an entered amount, a stored column. Trailing zeros beyond the paisa
    /// are fine (<c>5.000</c> is ₹5.00); a digit below the paisa is a bug in the caller and throws.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="amount"/> has a digit below the paisa.</exception>
    public static Money Of(decimal amount)
    {
        if (!TryOf(amount, out var money))
        {
            throw new ArgumentException(
                $"{amount.ToString(CultureInfo.InvariantCulture)} has digits below the paisa; money is exact to the paisa. Round an arithmetic result with Money.Round.",
                nameof(amount));
        }

        return money;
    }

    /// <summary>
    /// Whether <paramref name="amount"/> is money (no digit below the paisa), for the use case that turns input into a
    /// business failure instead of an exception. Trailing zeros are fine.
    /// </summary>
    public static bool TryOf(decimal amount, out Money money)
    {
        if (!DecimalScale.IsExact(amount, Scale))
        {
            money = default;
            return false;
        }

        money = new Money(amount);
        return true;
    }

    /// <summary>Money from an arithmetic result: rounded to two decimals, half away from zero (BR-001).</summary>
    public static Money Round(decimal raw) => new(Math.Round(raw, Scale, Rounding));

    /// <summary>Adds two amounts.</summary>
    public static Money operator +(Money left, Money right) => new(left.Amount + right.Amount);

    /// <summary>Subtracts <paramref name="right"/> from <paramref name="left"/>; the result may be negative.</summary>
    public static Money operator -(Money left, Money right) => new(left.Amount - right.Amount);

    /// <summary>The same amount with the opposite sign.</summary>
    public static Money operator -(Money value) => new(-value.Amount);

    /// <summary>Less than.</summary>
    public static bool operator <(Money left, Money right) => left.Amount < right.Amount;

    /// <summary>Less than or equal.</summary>
    public static bool operator <=(Money left, Money right) => left.Amount <= right.Amount;

    /// <summary>Greater than.</summary>
    public static bool operator >(Money left, Money right) => left.Amount > right.Amount;

    /// <summary>Greater than or equal.</summary>
    public static bool operator >=(Money left, Money right) => left.Amount >= right.Amount;

    /// <inheritdoc />
    public int CompareTo(Money other) => Amount.CompareTo(other.Amount);

    /// <summary>The amount with exactly two decimals and no grouping, whatever the culture: <c>1234.50</c>, <c>-0.05</c>.</summary>
    public override string ToString() => Amount.ToString("0.00", CultureInfo.InvariantCulture);
}
