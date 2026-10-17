namespace BillingRecon.Domain.Common;

/// <summary>
/// The one rule the decimal value objects share: a value is exact to <c>scale</c> decimals when no digit lies below
/// them. Trailing zeros are fine (<c>5.000</c> is exact to two decimals); <c>5.001</c> is not.
/// </summary>
internal static class DecimalScale
{
    /// <summary>True when <paramref name="value"/> has no digit below the <paramref name="scale"/>th decimal.</summary>
    public static bool IsExact(decimal value, int scale) => decimal.Round(value, scale) == value;
}
