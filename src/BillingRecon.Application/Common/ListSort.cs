using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Common;

/// <summary>
/// The order a list was asked for (ADR 0063): one of the columns its endpoint names, ascending, or descending when
/// the parameter begins with <c>-</c> (<c>sort=total</c>, <c>sort=-total</c>). The name is only ever compared with the
/// endpoint's own list and never reaches the database: each name stands for an expression written in the query
/// service. Every order ends on a tie-breaker the query service adds, so that two pages never share or skip a row.
/// </summary>
/// <param name="Column">The name of the column, as the endpoint lists it.</param>
/// <param name="Descending">True for the largest or latest first.</param>
public readonly record struct ListSort(string Column, bool Descending)
{
    /// <summary>Longest text of the parameter that is read at all.</summary>
    public const int MaxLength = 40;

    /// <summary>
    /// Reads <paramref name="text"/> against the columns an endpoint sorts by. No text is no order asked for: the
    /// endpoint's own, and <paramref name="sort"/> is null. Anything that is not one of <paramref name="columns"/>,
    /// whatever its case, is false with <paramref name="refusal"/>, 400 <c>sort_not_supported</c>, which names the
    /// columns.
    /// </summary>
    public static bool TryParse(string? text, IReadOnlyCollection<string> columns, out ListSort? sort, out Error? refusal)
    {
        ArgumentNullException.ThrowIfNull(columns);

        (sort, refusal) = (null, null);
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var trimmed = text.Trim();
        var descending = trimmed.StartsWith('-');
        var name = descending ? trimmed[1..] : trimmed;
        var column = trimmed.Length > MaxLength ? null : columns.FirstOrDefault(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase));
        if (column is null)
        {
            refusal = CommonErrors.SortNotSupported(trimmed.Length > MaxLength ? trimmed[..MaxLength] : trimmed, columns);
            return false;
        }

        sort = new ListSort(column, descending);
        return true;
    }
}
