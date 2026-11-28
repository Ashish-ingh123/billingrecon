using System.Globalization;
using System.Text;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// The rules of automatic matching (BR-1008 to BR-1011; ADR 0066, as approved for Prompt 13.2), pure: it reads the
/// unmatched lines of a channel and the payments or refunds no active match holds, and decides for each line.
/// <list type="bullet">
/// <item>It <b>confirms</b> only the certain case: the reference agrees, the amount is equal to the paisa, the item is
/// dated in the window, and exactly one item fits the line and exactly one line fits the item.</item>
/// <item>Otherwise it <b>proposes</b>, by the first rule that finds anything: (1) the reference agrees but something
/// else does not; (2) the amount is equal and the date in the window, with one to three candidates; (3) two to five
/// items of one payer in the window add up to the line. Scores are 90, 70 and 50, less 5 for each day between the item
/// and the line; they order the proposals of a line and decide nothing.</item>
/// </list>
/// A reference "agrees" when the line's reference equals the item's (without case, spaces and leading zeros), or when an
/// item's reference of at least <see cref="MinimumReferenceInDescription"/> characters is written inside the line's
/// description, as banks write a UTR in the narration.
/// </summary>
public static class StatementMatcher
{
    /// <summary>The shortest reference that is looked for inside a description: a UTR or a UPI reference is 12 to 22.</summary>
    public const int MinimumReferenceInDescription = 8;

    /// <summary>How many days before a line an item whose reference agrees is still proposed (rule 1).</summary>
    public const int ReferenceLookBackDays = 30;

    /// <summary>The most candidates of one amount rule 2 proposes; more are too many to choose from.</summary>
    public const int MostSameAmount = 3;

    /// <summary>The most items of one payer rule 3 combines, and the most items of a payer it tries combinations of.</summary>
    public const int MostInCombination = 5;

    private const int MostItemsOfPayerTried = 12;
    private const int MostProposalsOfRuleThree = 3;

    /// <summary>For each line, what the run does with it. Every line of <paramref name="lines"/> has an answer.</summary>
    public static IReadOnlyDictionary<StatementLineId, MatchDecision> Decide(
        IReadOnlyList<LineToMatch> lines,
        IReadOnlyList<MatchCandidate> candidates,
        int windowDays)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(candidates);

        var index = new CandidateIndex(candidates);
        var linesByAmount = lines.ToLookup(line => line.Amount);
        var free = new HashSet<Guid>(candidates.Select(candidate => candidate.Id));
        var decisions = new Dictionary<StatementLineId, MatchDecision>();

        // First the certain matches, of every line, so that a proposal never offers an item another line was given.
        foreach (var line in lines)
        {
            var sure = index.OfAmount(line.Amount)
                .Where(candidate => free.Contains(candidate.Id) && InWindow(line, candidate, windowDays) && ReferenceAgrees(line, candidate))
                .ToList();
            if (sure.Count != 1)
            {
                continue;
            }

            var item = sure[0];
            var rivals = linesByAmount[item.Amount].Count(other => InWindow(other, item, windowDays) && ReferenceAgrees(other, item));
            if (rivals != 1)
            {
                continue;
            }

            free.Remove(item.Id);
            decisions[line.Id] = MatchDecision.Confirm(
                [item],
                $"The reference {Shown(item.Reference)} and the amount {Rupees(item.Amount)} agree with {item.Number} of {Day(item.Date)}.");
        }

        foreach (var line in lines.Where(line => !decisions.ContainsKey(line.Id)))
        {
            decisions[line.Id] = Propose(line, index, free, windowDays);
        }

        return decisions;
    }

    private static MatchDecision Propose(LineToMatch line, CandidateIndex index, HashSet<Guid> free, int windowDays)
    {
        // Rule 1: the reference agrees, and something else does not (or another line has it too).
        var byReference = index.ByReferenceOf(line)
            .Where(candidate => free.Contains(candidate.Id) && ReferenceAgrees(line, candidate))
            .Where(candidate => candidate.Date <= line.Date && candidate.Date >= line.Date.AddDays(-ReferenceLookBackDays))
            .Select(candidate => new ProposedMatch(1, Score(90, line, candidate), [candidate], ReferenceReason(line, candidate, windowDays)))
            .ToList();
        if (byReference.Count > 0)
        {
            return MatchDecision.ProposeAll(byReference);
        }

        // Rule 2: the amount and the date, with one to three candidates.
        var byAmount = index.OfAmount(line.Amount).Where(candidate => free.Contains(candidate.Id) && InWindow(line, candidate, windowDays)).ToList();
        if (byAmount.Count is > 0 and <= MostSameAmount)
        {
            var others = byAmount.Count == 1 ? string.Empty : $" {byAmount.Count} items of this amount fit; check which.";
            return MatchDecision.ProposeAll([.. byAmount.Select(candidate => new ProposedMatch(
                2,
                Score(70, line, candidate),
                [candidate],
                $"The amount {Rupees(candidate.Amount)} agrees with {candidate.Number} of {Day(candidate.Date)}, {DaysBefore(line, candidate)}.{others}"))]);
        }

        // Rule 3: two to five items of one payer in the window that add up to the line.
        var combinations = new List<ProposedMatch>();
        foreach (var payer in index.InWindow(line, windowDays)
            .Where(candidate => free.Contains(candidate.Id) && candidate.Amount < line.Amount)
            .GroupBy(candidate => candidate.PayerId)
            .Where(group => group.Count() >= 2))
        {
            var items = payer.OrderBy(candidate => candidate.Date).ThenBy(candidate => candidate.Number, StringComparer.Ordinal).Take(MostItemsOfPayerTried).ToArray();
            if (FirstCombination(items, line.Amount) is { } found)
            {
                var latest = found.Max(candidate => line.Date.DayNumber - candidate.Date.DayNumber);
                combinations.Add(new ProposedMatch(
                    3,
                    Math.Max(0, 50 - (5 * latest)),
                    found,
                    $"{found.Count} items of one payer add up to {Rupees(line.Amount)}: {string.Join(", ", found.Select(candidate => candidate.Number))}."));
            }

            if (combinations.Count == MostProposalsOfRuleThree)
            {
                break;
            }
        }

        return combinations.Count > 0 ? MatchDecision.ProposeAll(combinations) : MatchDecision.Nothing;
    }

    // The first set of 2 to 5 items whose amounts add up to the target, the earliest items first.
    private static List<MatchCandidate>? FirstCombination(MatchCandidate[] items, Money target)
    {
        for (var size = 2; size <= Math.Min(MostInCombination, items.Length); size++)
        {
            if (Search(items, target, size, 0, []) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static List<MatchCandidate>? Search(MatchCandidate[] items, Money remaining, int size, int from, List<MatchCandidate> chosen)
    {
        if (chosen.Count == size)
        {
            return remaining == Money.Zero ? [.. chosen] : null;
        }

        for (var index = from; index < items.Length; index++)
        {
            if (items[index].Amount > remaining)
            {
                continue;
            }

            chosen.Add(items[index]);
            var found = Search(items, remaining - items[index].Amount, size, index + 1, chosen);
            chosen.RemoveAt(chosen.Count - 1);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static string ReferenceReason(LineToMatch line, MatchCandidate candidate, int windowDays)
    {
        var start = $"The reference {Shown(candidate.Reference)} agrees with {candidate.Number} of {Day(candidate.Date)}";
        if (candidate.Amount != line.Amount)
        {
            return $"{start}, but its amount is {Rupees(candidate.Amount)} and the line's {Rupees(line.Amount)}.";
        }

        if (!InWindow(line, candidate, windowDays))
        {
            return $"{start}, but it is dated {DaysBefore(line, candidate)}, outside the window of {windowDays} days.";
        }

        return $"{start}, but another line or item has the same reference and amount; check which.";
    }

    /// <summary>Whether the reference of <paramref name="candidate"/> agrees with the line's (see the class).</summary>
    public static bool ReferenceAgrees(LineToMatch line, MatchCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(candidate);

        var reference = Normal(candidate.Reference);
        if (reference.Length == 0)
        {
            return false;
        }

        if (Normal(line.Reference) == reference)
        {
            return true;
        }

        // Inside the description, the reference is compared as written there, leading zeros and all.
        var written = Compact(candidate.Reference);
        return written.Length >= MinimumReferenceInDescription && Compact(line.Description).Contains(written, StringComparison.Ordinal);
    }

    private static bool InWindow(LineToMatch line, MatchCandidate candidate, int windowDays)
        => candidate.Date <= line.Date && candidate.Date >= line.Date.AddDays(-windowDays);

    private static int Score(int top, LineToMatch line, MatchCandidate candidate)
        => Math.Clamp(top - (5 * Math.Max(0, line.Date.DayNumber - candidate.Date.DayNumber)), 0, 100);

    // Upper case, without spaces; and without leading zeros, as a cheque number is written with or without them.
    private static string Normal(string? reference) => Compact(reference).TrimStart('0');

    private static string Compact(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var compact = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (!char.IsWhiteSpace(character))
            {
                compact.Append(char.ToUpperInvariant(character));
            }
        }

        return compact.ToString();
    }

    private static string Shown(string? reference) => reference?.Trim() ?? string.Empty;

    private static string Rupees(Money amount) => "₹" + amount.Amount.ToString("#,##0.00", CultureInfo.GetCultureInfo("en-IN"));

    private static string Day(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    private static string DaysBefore(LineToMatch line, MatchCandidate candidate)
    {
        var days = line.Date.DayNumber - candidate.Date.DayNumber;
        return days switch
        {
            0 => "the same day",
            1 => "1 day before the line",
            > 0 => $"{days} days before the line",
            _ => $"{-days} days after the line",
        };
    }
}
