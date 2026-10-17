namespace BillingRecon.Domain.Common;

/// <summary>Sums over sequences of <see cref="Money"/>, in the shape LINQ readers expect.</summary>
public static class MoneyExtensions
{
    /// <summary>The sum of <paramref name="amounts"/>; zero for none. Sums of money are exact and are never rounded (BR-203).</summary>
    public static Money Sum(this IEnumerable<Money> amounts)
    {
        ArgumentNullException.ThrowIfNull(amounts);

        var total = Money.Zero;
        foreach (var amount in amounts)
        {
            total += amount;
        }

        return total;
    }
}
