using BillingRecon.Application.Reconciliation;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Reconciliation;

/// <summary>Adds imports and their lines through the request's context, and answers what a channel already holds; filtered by its tenant.</summary>
public sealed class StatementRepository(BillingReconDbContext context) : IStatementRepository
{
    // SQL Server takes at most 2,100 parameters in a statement; the fingerprints are asked for in batches well under it.
    private const int FingerprintBatch = 1000;

    /// <inheritdoc />
    public Task<bool> FileImportedAsync(SettlementChannelId channelId, string fileHash, CancellationToken ct)
        => context.Set<StatementImport>().AsNoTracking().AnyAsync(import => import.ChannelId == channelId && import.FileHash == fileHash, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, int>> HeldCountsAsync(SettlementChannelId channelId, IReadOnlyCollection<string> fingerprints, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fingerprints);

        var held = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var batch in fingerprints.Chunk(FingerprintBatch))
        {
            var counts = await context.Set<StatementLine>().AsNoTracking()
                .Where(line => line.ChannelId == channelId && batch.Contains(line.Fingerprint))
                .GroupBy(line => line.Fingerprint)
                .Select(group => new { Fingerprint = group.Key, Count = group.Count() })
                .ToListAsync(ct);
            foreach (var count in counts)
            {
                held[count.Fingerprint] = count.Count;
            }
        }

        return held;
    }

    /// <inheritdoc />
    public void Add(StatementImport import) => context.Set<StatementImport>().Add(import);

    /// <inheritdoc />
    public void AddLines(IReadOnlyCollection<StatementLine> lines) => context.Set<StatementLine>().AddRange(lines);
}
