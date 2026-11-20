namespace BillingRecon.Application.Common;

/// <summary>
/// One page of a list (docs/api.md): the items in a stable order, the number of the page (from 1), the page size that
/// was asked for, and how many items all pages hold together.
/// </summary>
/// <typeparam name="T">The type of the items.</typeparam>
public sealed record PageOf<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
