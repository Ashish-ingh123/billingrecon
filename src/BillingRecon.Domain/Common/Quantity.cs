using System.Globalization;

namespace BillingRecon.Domain.Common;

/// <summary>
/// How many units a bill line charges: more than zero, exact to three decimals (BR-201, ADR 0004). Stored as
/// <c>decimal(9,3)</c>. The default value of the struct (zero) is not a valid quantity; only <see cref="Of"/> and
/// <see cref="TryOf"/> produce one, and persistence reads stored values through <see cref="Of"/>.
/// </summary>
public readonly record struct Quantity : IComparable<Quantity>
{
    /// <summary>Decimals a quantity is exact to.</summary>
    public const int Scale = 3;

    private Quantity(decimal value) => Value = value;

    /// <summary>The number of units, above zero, with no digit below the third decimal.</summary>
    public decimal Value { get; }

    /// <summary>A quantity from a value that is one. Zero, a negative value, or a digit below the third decimal, is a bug in the caller and throws.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is zero or negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> has a digit below the third decimal.</exception>
    public static Quantity Of(decimal value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        if (!TryOf(value, out var quantity))
        {
            throw new ArgumentException($"{value.ToString(CultureInfo.InvariantCulture)} has a digit below the third decimal.", nameof(value));
        }

        return quantity;
    }

    /// <summary>Whether <paramref name="value"/> is a valid quantity, for the use case that turns input into a business failure instead of an exception.</summary>
    public static bool TryOf(decimal value, out Quantity quantity)
    {
        if (value <= 0m || !DecimalScale.IsExact(value, Scale))
        {
            quantity = default;
            return false;
        }

        quantity = new Quantity(value);
        return true;
    }

    /// <summary>Less than.</summary>
    public static bool operator <(Quantity left, Quantity right) => left.Value < right.Value;

    /// <summary>Less than or equal.</summary>
    public static bool operator <=(Quantity left, Quantity right) => left.Value <= right.Value;

    /// <summary>Greater than.</summary>
    public static bool operator >(Quantity left, Quantity right) => left.Value > right.Value;

    /// <summary>Greater than or equal.</summary>
    public static bool operator >=(Quantity left, Quantity right) => left.Value >= right.Value;

    /// <inheritdoc />
    public int CompareTo(Quantity other) => Value.CompareTo(other.Value);

    /// <summary>The quantity with exactly three decimals, whatever the culture: <c>2.500</c>.</summary>
    public override string ToString() => Value.ToString("0.000", CultureInfo.InvariantCulture);
}
