using System.Globalization;

namespace BillingRecon.Domain.Common;

/// <summary>
/// The price of one unit of a service or item: not negative, exact to four decimals (per-unit drug prices, per-minute
/// charges; BR-201, ADR 0004). A line amount is <c>Money.Round(quantity × unit price)</c>: the product is a raw
/// decimal until it is rounded, which is why there is no arithmetic here. Stored as <c>decimal(19,4)</c>.
/// </summary>
public readonly record struct UnitPrice : IComparable<UnitPrice>
{
    /// <summary>Decimals a unit price is exact to.</summary>
    public const int Scale = 4;

    private UnitPrice(decimal value) => Value = value;

    /// <summary>A free unit.</summary>
    public static UnitPrice Zero { get; } = new(0m);

    /// <summary>The price in rupees, with no digit below the fourth decimal.</summary>
    public decimal Value { get; }

    /// <summary>A unit price from a value that is one. A negative value, or a digit below the fourth decimal, is a bug in the caller and throws.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> has a digit below the fourth decimal.</exception>
    public static UnitPrice Of(decimal value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        if (!TryOf(value, out var price))
        {
            throw new ArgumentException($"{value.ToString(CultureInfo.InvariantCulture)} has a digit below the fourth decimal.", nameof(value));
        }

        return price;
    }

    /// <summary>Whether <paramref name="value"/> is a valid unit price, for the use case that turns input into a business failure instead of an exception.</summary>
    public static bool TryOf(decimal value, out UnitPrice price)
    {
        if (value < 0m || !DecimalScale.IsExact(value, Scale))
        {
            price = default;
            return false;
        }

        price = new UnitPrice(value);
        return true;
    }

    /// <summary>Less than.</summary>
    public static bool operator <(UnitPrice left, UnitPrice right) => left.Value < right.Value;

    /// <summary>Less than or equal.</summary>
    public static bool operator <=(UnitPrice left, UnitPrice right) => left.Value <= right.Value;

    /// <summary>Greater than.</summary>
    public static bool operator >(UnitPrice left, UnitPrice right) => left.Value > right.Value;

    /// <summary>Greater than or equal.</summary>
    public static bool operator >=(UnitPrice left, UnitPrice right) => left.Value >= right.Value;

    /// <inheritdoc />
    public int CompareTo(UnitPrice other) => Value.CompareTo(other.Value);

    /// <summary>The price with exactly four decimals, whatever the culture: <c>333.3333</c>.</summary>
    public override string ToString() => Value.ToString("0.0000", CultureInfo.InvariantCulture);
}
