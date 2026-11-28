using System.Text;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// The candidates of a matching run, found by amount, by reference and by day, so that each line looks at the few items
/// that can fit it rather than at all of them (10,000 lines against 10,000 payments, Prompt 13.2).
/// </summary>
internal sealed class CandidateIndex
{
    private readonly ILookup<Money, MatchCandidate> _byAmount;
    private readonly ILookup<string, MatchCandidate> _byReference;
    private readonly ILookup<DateOnly, MatchCandidate> _byDay;

    public CandidateIndex(IReadOnlyList<MatchCandidate> candidates)
    {
        _byAmount = candidates.ToLookup(candidate => candidate.Amount);
        _byReference = candidates.Where(candidate => Key(candidate.Reference).Length > 0).ToLookup(candidate => Key(candidate.Reference), StringComparer.Ordinal);
        _byDay = candidates.ToLookup(candidate => candidate.Date);
    }

    /// <summary>The candidates of exactly this amount.</summary>
    public IEnumerable<MatchCandidate> OfAmount(Money amount) => _byAmount[amount];

    /// <summary>The candidates dated in the line's window: the days from the line back by the window.</summary>
    public IEnumerable<MatchCandidate> InWindow(LineToMatch line, int windowDays)
        => Enumerable.Range(0, windowDays + 1).SelectMany(days => _byDay[line.Date.AddDays(-days)]);

    /// <summary>
    /// The candidates whose reference may agree with the line: the line's own reference, and each word of its description
    /// (a reference written in a narration stands between separators); <see cref="StatementMatcher.ReferenceAgrees"/>
    /// decides.
    /// </summary>
    public IEnumerable<MatchCandidate> ByReferenceOf(LineToMatch line)
    {
        var keys = Words(line.Description).Append(Key(line.Reference)).Where(key => key.Length > 0).Distinct(StringComparer.Ordinal);
        return keys.SelectMany(key => _byReference[key]).Distinct();
    }

    // Upper case, without spaces and leading zeros: as StatementMatcher compares a reference.
    private static string Key(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return string.Empty;
        }

        var key = new StringBuilder(reference.Length);
        foreach (var character in reference.Where(character => !char.IsWhiteSpace(character)))
        {
            key.Append(char.ToUpperInvariant(character));
        }

        return key.ToString().TrimStart('0');
    }

    // The words of a description: what stands between characters that are neither letters nor digits.
    private static IEnumerable<string> Words(string description)
    {
        var word = new StringBuilder();
        foreach (var character in description)
        {
            if (char.IsLetterOrDigit(character))
            {
                word.Append(char.ToUpperInvariant(character));
                continue;
            }

            if (word.Length > 0)
            {
                yield return word.ToString().TrimStart('0');
                word.Clear();
            }
        }

        if (word.Length > 0)
        {
            yield return word.ToString().TrimStart('0');
        }
    }
}
