using System.Globalization;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Common;

/// <summary>
/// The one search box of a list of documents (Prompt 11.1; ADR 0036, amended 2026-11-26): the text finds a document by
/// a part of its number, of its patient's name or MRN, of its payer's name, or, when it reads as an amount, by exactly
/// that amount. The rules of the text are here; how a list asks the database is in the read services.
/// </summary>
public static class DocumentSearch
{
    /// <summary>The longest text a list accepts; a longer one is a validation failure.</summary>
    public const int MaxLength = 100;

    /// <summary>
    /// The shortest text that is searched for. One character is in almost every number and name, so it would read
    /// every document of the hospital to leave almost all of them in: a shorter text filters nothing.
    /// </summary>
    public const int MinLength = 2;

    /// <summary>The text to search for, trimmed; null when there is none or it is too short to filter.</summary>
    /// <param name="search">The text as the request gave it.</param>
    /// <returns>The text, or null for a list without a search.</returns>
    public static string? Term(string? search)
    {
        var term = search?.Trim();

        return term is { Length: >= MinLength } ? term : null;
    }

    /// <summary>
    /// The amount the text states, when it is one: digits, at most one point and two decimals, with or without the
    /// commas of the Indian grouping. "1,250.50" and "1250.5" are 1250.50; "12.345", "-5" and "REF 12" are no amount.
    /// </summary>
    /// <param name="term">The text of the search.</param>
    /// <returns>The amount, or null.</returns>
    public static Money? Amount(string term)
    {
        ArgumentNullException.ThrowIfNull(term);

        var digits = term.Replace(",", string.Empty, StringComparison.Ordinal);

        return digits.Length > 0
            && digits.All(character => character is (>= '0' and <= '9') or '.')
            && decimal.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            && Money.TryOf(amount, out var money)
                ? money
                : null;
    }
}
