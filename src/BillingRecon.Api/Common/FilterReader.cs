using System.Globalization;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Common;

/// <summary>
/// Reads the filters of a list that Prompt 12.2 added, from the text of the query string (ADR 0063). They are bound as
/// text so that a malformed one is answered by the API in its own words: 400 <c>filter_invalid</c>, with a message for
/// each parameter, where a binder would have answered for all of them alike. An empty or missing parameter filters
/// nothing. The filters a list had before are bound and refused as they always were.
/// </summary>
public sealed class FilterReader
{
    /// <summary>The largest amount a filter may name: what a money column holds.</summary>
    public const decimal MaxAmount = 9_999_999_999_999.99m;

    private readonly Dictionary<string, string[]> _problems = new(StringComparer.Ordinal);
    private Error? _sort;

    /// <summary>An amount in rupees: digits with at most two decimals, from 0 to <see cref="MaxAmount"/>.</summary>
    public decimal? Amount(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            && amount is >= 0m and <= MaxAmount
            && decimal.Round(amount, 2) == amount)
        {
            return amount;
        }

        _problems[name] = ["An amount is a number from 0 with at most two decimals, for example 1500 or 1500.50."];
        return null;
    }

    /// <summary>A business date, as <c>YYYY-MM-DD</c>.</summary>
    public DateOnly? Date(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        _problems[name] = ["A date is written YYYY-MM-DD, for example 2026-11-27."];
        return null;
    }

    /// <summary>The id of a record: a UUID that is not the empty one.</summary>
    public Guid? Id(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (Guid.TryParseExact(text.Trim(), "D", out var id) && id != Guid.Empty)
        {
            return id;
        }

        _problems[name] = ["An id is a UUID, for example 3f2504e0-4f89-11d3-9a0c-0305e82c3301."];
        return null;
    }

    /// <summary>One of the names of <typeparamref name="TEnum"/>, whatever its case; never a number.</summary>
    public TEnum? OneOf<TEnum>(string? text, string name)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        if (!char.IsAsciiDigit(trimmed[0]) && trimmed[0] != '-' && Enum.TryParse<TEnum>(trimmed, ignoreCase: true, out var value) && Enum.IsDefined(value))
        {
            return value;
        }

        _problems[name] = [$"One of: {string.Join(", ", Enum.GetNames<TEnum>())}."];
        return null;
    }

    /// <summary>A range's end is not before its start; said at the end, <paramref name="toName"/>.</summary>
    public void InOrder<T>(T? from, T? to, string toName)
        where T : struct, IComparable<T>
    {
        if (from is { } start && to is { } end && end.CompareTo(start) < 0)
        {
            _problems.TryAdd(toName, ["The end of a range is not before its start."]);
        }
    }

    /// <summary>The order asked for, one of <paramref name="columns"/>; null for the list's own order.</summary>
    public ListSort? Sort(string? text, IReadOnlyCollection<string> columns)
    {
        if (!ListSort.TryParse(text, columns, out var sort, out var refusal))
        {
            _sort = refusal;
        }

        return sort;
    }

    /// <summary><paramref name="query"/>, or the refusal that names every filter that could not be read.</summary>
    public Result<TQuery> Answer<TQuery>(TQuery query)
        => _problems.Count > 0 ? CommonErrors.FilterInvalid(_problems) : _sort is { } sort ? sort : Result.Success(query);
}
