using BillingRecon.Application.Common;
using BillingRecon.Application.Reconciliation;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Reconciliation;

/// <summary>Reads imports and statement lines through the request's context, untracked and filtered by its tenant (BR-1024).</summary>
public sealed class StatementReadService(BillingReconDbContext context) : IStatementReadService
{
    /// <inheritdoc />
    public async Task<StatementImportDto?> GetImportAsync(StatementImportId id, CancellationToken ct)
    {
        var import = await context.Set<StatementImport>().AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, ct);
        return import?.ToDto();
    }

    /// <inheritdoc />
    public async Task<PageOf<StatementImportDto>> ListImportsAsync(ListStatementImportsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var imports = context.Set<StatementImport>().AsNoTracking();
        if (query.ChannelId is { } channel)
        {
            var channelId = new SettlementChannelId(channel);
            imports = imports.Where(import => import.ChannelId == channelId);
        }

        var totalCount = await imports.CountAsync(ct);
        var page = await imports
            .OrderByDescending(import => import.CreatedAtUtc)
            .ThenByDescending(import => import.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PageOf<StatementImportDto>([.. page.Select(import => import.ToDto())], query.Page, query.PageSize, totalCount);
    }

    /// <inheritdoc />
    public async Task<PageOf<StatementLineDto>> ListLinesAsync(ListStatementLinesQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var lines = context.Set<StatementLine>().AsNoTracking();
        if (query.ChannelId is { } channel)
        {
            var channelId = new SettlementChannelId(channel);
            lines = lines.Where(line => line.ChannelId == channelId);
        }

        if (query.ImportId is { } import)
        {
            var importId = new StatementImportId(import);
            lines = lines.Where(line => line.ImportId == importId);
        }

        if (query.Status is { } status)
        {
            lines = lines.Where(line => line.Status == status);
        }

        if (query.Direction is { } direction)
        {
            lines = lines.Where(line => line.Direction == direction);
        }

        if (query.From is { } from)
        {
            lines = lines.Where(line => line.TransactionDate >= from);
        }

        if (query.To is { } to)
        {
            lines = lines.Where(line => line.TransactionDate <= to);
        }

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            // The search text is data, never a pattern.
            var pattern = LikePattern.Containing(search);
            lines = lines.Where(line =>
                (line.Reference != null && EF.Functions.Like(line.Reference, pattern, LikePattern.Escape)) || EF.Functions.Like(line.Description, pattern, LikePattern.Escape));
        }

        var totalCount = await lines.CountAsync(ct);
        IOrderedQueryable<StatementLine> ordered = query.Order switch
        {
            { Column: "transactionDate", Descending: var down } => lines.By(line => line.TransactionDate, down).Then(line => line.RowNumber, down).Then(line => line.Id, down),
            { Column: "amount", Descending: var down } => lines.By(line => line.Amount, down).Then(line => line.Id, down),
            null => lines.OrderByDescending(line => line.TransactionDate).ThenByDescending(line => line.RowNumber).ThenByDescending(line => line.Id),
            _ => throw new InvalidOperationException($"The order {query.Order} is not one of this list; its validator should have refused it."),
        };

        var page = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        // What each line of the page is matched to, or what is proposed for it (ADR 0066): two small reads by the page's ids.
        var ids = page.Select(line => line.Id).ToList();
        var matched = await context.Set<MatchLine>().AsNoTracking()
            .Where(link => link.IsActive && ids.Contains(link.StatementLineId))
            .Join(context.Set<Match>().AsNoTracking(), link => link.MatchId, match => match.Id, (link, match) => new { link.StatementLineId, match.Reason })
            .ToListAsync(ct);
        var proposed = await context.Set<MatchProposal>().AsNoTracking()
            .Where(proposal => proposal.Status == MatchProposalStatus.Open && ids.Contains(proposal.StatementLineId))
            .Select(proposal => new { proposal.StatementLineId, proposal.Score, proposal.Reason })
            .ToListAsync(ct);
        var reasons = matched.ToDictionary(row => row.StatementLineId, row => row.Reason);
        var proposals = proposed.GroupBy(row => row.StatementLineId).ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.Score).ToList());

        return new PageOf<StatementLineDto>(
            [
                .. page.Select(line =>
                {
                    var best = proposals.GetValueOrDefault(line.Id);
                    return line.ToDto() with
                    {
                        MatchReason = reasons.GetValueOrDefault(line.Id),
                        ProposalReason = best?[0].Reason,
                        ProposalScore = best?[0].Score,
                        ProposalCount = best?.Count ?? 0,
                    };
                }),
            ],
            query.Page,
            query.PageSize,
            totalCount);
    }
}
