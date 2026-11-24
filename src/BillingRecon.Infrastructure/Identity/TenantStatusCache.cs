using System.Collections.Concurrent;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// Whether each tenant was active when last read, kept for <see cref="Lifetime"/> (ADR 0052): a suspension reaches every
/// instance of the API within that time without a read of the tenant's row on every request. In memory, per instance.
/// </summary>
public sealed class TenantStatusCache(IClock clock)
{
    /// <summary>How long an answer is kept.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<TenantId, (bool Active, DateTimeOffset ReadAtUtc)> _answers = new();

    /// <summary>The kept answer for the tenant, if one is younger than <see cref="Lifetime"/>.</summary>
    public bool? Find(TenantId tenantId) =>
        _answers.TryGetValue(tenantId, out var answer) && clock.UtcNow - answer.ReadAtUtc < Lifetime ? answer.Active : null;

    /// <summary>Keeps the answer just read.</summary>
    public void Keep(TenantId tenantId, bool active) => _answers[tenantId] = (active, clock.UtcNow);
}
