using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// Adds imports and their lines, and answers what a channel already holds (BR-1004). Reads are filtered by the tenant.
/// Nothing here removes or changes a line: its read fields never change, and its status follows its matches (13.2).
/// </summary>
public interface IStatementRepository
{
    /// <summary>Whether the channel already took a file with this SHA-256.</summary>
    public Task<bool> FileImportedAsync(SettlementChannelId channelId, string fileHash, CancellationToken ct);

    /// <summary>For each of <paramref name="fingerprints"/>, how many lines of the channel already have it; a fingerprint the channel has never held is absent.</summary>
    public Task<IReadOnlyDictionary<string, int>> HeldCountsAsync(SettlementChannelId channelId, IReadOnlyCollection<string> fingerprints, CancellationToken ct);

    /// <summary>Adds a new import to the unit of work, which gives it its id at once; it is saved by the commit.</summary>
    public void Add(StatementImport import);

    /// <summary>Adds the lines of an import to the unit of work; they are saved by the commit.</summary>
    public void AddLines(IReadOnlyCollection<StatementLine> lines);
}
