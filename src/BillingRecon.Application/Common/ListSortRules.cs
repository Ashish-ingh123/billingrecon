using FluentValidation;

namespace BillingRecon.Application.Common;

/// <summary>The rule of a list's order (ADR 0063): none, or one of the columns the list names.</summary>
public static class ListSortRules
{
    /// <summary>An order is one of <paramref name="columns"/>. The API has refused any other with 400 <c>sort_not_supported</c> before a query is made.</summary>
    public static IRuleBuilderOptions<T, ListSort?> OneOf<T>(this IRuleBuilder<T, ListSort?> rule, IReadOnlyCollection<string> columns)
        => rule
            .Must(order => order is null || columns.Contains(order.Value.Column, StringComparer.Ordinal))
            .WithMessage($"The order is one of: {string.Join(", ", columns)}.");
}
