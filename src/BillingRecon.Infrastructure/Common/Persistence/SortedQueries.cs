using System.Linq.Expressions;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>
/// The order of a list that was asked for by name (ADR 0063). A query service maps each name its endpoint offers to a
/// key written here in code, ascending or descending, and always ends on a key that is one of a kind, so that the
/// rows of two pages never overlap.
/// </summary>
internal static class SortedQueries
{
    /// <summary>Orders by <paramref name="key"/>, the largest first when <paramref name="descending"/>.</summary>
    public static IOrderedQueryable<T> By<T, TKey>(this IQueryable<T> source, Expression<Func<T, TKey>> key, bool descending)
        => descending ? source.OrderByDescending(key) : source.OrderBy(key);

    /// <summary>Then by <paramref name="key"/>, in the same direction as the first key.</summary>
    public static IOrderedQueryable<T> Then<T, TKey>(this IOrderedQueryable<T> source, Expression<Func<T, TKey>> key, bool descending)
        => descending ? source.ThenByDescending(key) : source.ThenBy(key);
}
