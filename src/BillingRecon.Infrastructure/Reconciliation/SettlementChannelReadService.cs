using BillingRecon.Application.Common;
using BillingRecon.Application.Reconciliation;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Reconciliation;

/// <summary>Reads settlement channels through the request's context, untracked and filtered by its tenant.</summary>
public sealed class SettlementChannelReadService(BillingReconDbContext context) : ISettlementChannelReadService
{
    /// <inheritdoc />
    public async Task<SettlementChannelDto?> GetAsync(SettlementChannelId id, CancellationToken ct)
    {
        var channel = await Channels().SingleOrDefaultAsync(candidate => candidate.Id == id, ct);
        return channel?.ToDto();
    }

    /// <inheritdoc />
    public async Task<PageOf<SettlementChannelDto>> ListAsync(ListSettlementChannelsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var channels = Channels();
        if (query.Kind is { } kind)
        {
            channels = channels.Where(channel => channel.Kind == kind);
        }

        if (query.Status is { } status)
        {
            channels = channels.Where(channel => channel.Status == status);
        }

        var totalCount = await channels.CountAsync(ct);
        var page = await channels
            .OrderBy(channel => channel.Name)
            .ThenBy(channel => channel.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PageOf<SettlementChannelDto>([.. page.Select(channel => channel.ToDto())], query.Page, query.PageSize, totalCount);
    }

    /// <inheritdoc />
    public Task<bool> NameExistsAsync(string name, SettlementChannelId? except, CancellationToken ct)
        => except is { } id
            ? Channels().AnyAsync(channel => channel.Name == name && channel.Id != id, ct)
            : Channels().AnyAsync(channel => channel.Name == name, ct);

    /// <inheritdoc />
    public Task<bool> IsActiveBankAsync(SettlementChannelId id, CancellationToken ct)
        => Channels().AnyAsync(channel => channel.Id == id && channel.Kind == SettlementChannelKind.Bank && channel.Status == SettlementChannelStatus.Active, ct);

    private IQueryable<SettlementChannel> Channels() => context.Set<SettlementChannel>().AsNoTracking();
}
