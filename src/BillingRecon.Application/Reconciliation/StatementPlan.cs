using System.Security.Cryptography;
using System.Text;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// What an import of a file would do with each line read (BR-1004), the same for the dry run and the import: a line is
/// the n-th of its fingerprint in the file; when the channel already holds n or more lines with that fingerprint, it was
/// imported before and is not stored again; otherwise it is stored as occurrence n.
/// </summary>
public static class StatementPlan
{
    /// <summary>The SHA-256 of a file's text in UTF-8, in upper-case hexadecimal: the same file has the same hash.</summary>
    public static string FileHashOf(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Each line read, with its fingerprint, its occurrence in the file and whether the channel already holds it.</summary>
    public static IReadOnlyList<PlannedStatementLine> Plan(SettlementChannelId channelId, IReadOnlyList<ReadStatementRow> lines, IReadOnlyDictionary<string, int> held)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(held);

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var planned = new List<PlannedStatementLine>(lines.Count);
        foreach (var line in lines)
        {
            var fingerprint = StatementLine.FingerprintOf(channelId, line.Values);
            var occurrence = seen[fingerprint] = seen.GetValueOrDefault(fingerprint) + 1;
            planned.Add(new PlannedStatementLine(line, fingerprint, occurrence, occurrence <= held.GetValueOrDefault(fingerprint)));
        }

        return planned;
    }

    /// <summary>The distinct fingerprints of <paramref name="lines"/>, to ask the channel how many of each it holds.</summary>
    public static IReadOnlyCollection<string> Fingerprints(SettlementChannelId channelId, IReadOnlyList<ReadStatementRow> lines)
        => [.. lines.Select(line => StatementLine.FingerprintOf(channelId, line.Values)).Distinct(StringComparer.Ordinal)];
}
