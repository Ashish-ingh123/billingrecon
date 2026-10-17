using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Common;

/// <summary>
/// The tenant the current unit of work acts for (BR-002, ADR 0009). In a request it comes from the validated token
/// and from nowhere else, resolved once per request.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// The current tenant. Null when the scope has none: system work (seeding, migrations, background jobs), which
    /// may then touch only tables that are not tenant-scoped. Reading or saving a tenant-scoped entity without a
    /// tenant fails.
    /// </summary>
    public TenantId? TenantId { get; }
}
