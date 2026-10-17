using System.Globalization;

namespace BillingRecon.Domain.Common;

/// <summary>
/// A percentage between 0 and 100, exact to two decimals: a tax rate (BR-201, ADR 0004). Stored as
/// <c>decimal(5,2)</c>. A tax amount is <c>Money.Round(taxable × rate / 100)</c>; the division is done on the raw
/// decimals, which is why there is no arithmetic here.
/// </summary>
public readonly record struct Percent : IComparable<Percent>
{
    /// <summary>Decimals a percentage is exact to.</summary>
    public const int Scale = 2;

    /// <summary>The largest percentage: everything.</summary>
    public const decimal Maximum = 100m;

    private Percent(decimal value) => Value = value;

    /// <summary>Nothing: zero per cent.</summary>
    public static Percent Zero { get; } = new(0m);

    /// <summary>The percentage, between 0 and 100, with no digit below the second decimal.</summary>
    public decimal Value { get; }

    /// <summary>A percentage from a value that is one. Outside 0 to 100, or with a digit below the second decimal, is a bug in the caller and throws.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is below 0 or above 100.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> has a digit below the second decimal.</exception>
    public static Percent Of(decimal value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, Maximum);
        if (!TryOf(value, out var percent))
        {
            throw new ArgumentException($"{value.ToString(CultureInfo.InvariantCulture)} has a digit below the second decimal.", nameof(value));
        }

        return percent;
    }

    /// <summary>Whether <paramref name="value"/> is a valid percentage, for the use case that turns input into a business failure instead of an exception.</summary>
    public static bool TryOf(decimal value, out Percent percent)
    {
        if (value < 0m || value > Maximum || !DecimalScale.IsExact(value, Scale))
        {
            percent = default;
            return false;
        }

        percent = new Percent(value);
        return true;
    }

    /// <summary>Less than.</summary>
    public static bool operator <(Percent left, Percent right) => left.Value < right.Value;

    /// <summary>Less than or equal.</summary>
    public static bool operator <=(Percent left, Percent right) => left.Value <= right.Value;

    /// <summary>Greater than.</summary>
    public static bool operator >(Percent left, Percent right) => left.Value > right.Value;

    /// <summary>Greater than or equal.</summary>
    public static bool operator >=(Percent left, Percent right) => left.Value >= right.Value;

    /// <inheritdoc />
    public int CompareTo(Percent other) => Value.CompareTo(other.Value);

    /// <summary>The percentage with exactly two decimals, whatever the culture: <c>18.00</c>.</summary>
    public override string ToString() => Value.ToString("0.00", CultureInfo.InvariantCulture);
}
