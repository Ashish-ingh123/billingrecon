using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>The row of the current tenant, to change its settings (BR-607, ADR 0033).</summary>
public interface ITenantRepository
{
    /// <summary>The tenant of the current scope, tracked by the unit of work. Throws when the scope has no tenant row.</summary>
    public Task<Tenant> GetCurrentAsync(CancellationToken ct);
}
