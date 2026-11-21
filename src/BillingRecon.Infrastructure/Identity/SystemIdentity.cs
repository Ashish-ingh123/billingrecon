using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// The identity of system work that no user started and that belongs to no tenant: the EF Core tools, seeding, and
/// later background jobs (ADR 0009). A context created with it can write tables that are not tenant-scoped, its
/// audit columns record "no user", and any attempt to read or write tenant-scoped data fails: the context attaches
/// the guards to itself, so this holds for a context built by hand as well. It is never registered as the identity
/// of a request; code that needs it creates it on purpose.
/// </summary>
public sealed class SystemIdentity : ITenantContext, ICurrentUser
{
    /// <summary>The single instance.</summary>
    public static SystemIdentity Instance { get; } = new();

    private SystemIdentity()
    {
    }

    /// <summary>Always null: system work has no tenant.</summary>
    public TenantId? TenantId => null;

    /// <summary>Always null: no user is acting.</summary>
    public UserId? UserId => null;

    /// <summary>Always none: no user is acting, so nothing is decided by a role.</summary>
    public IReadOnlyCollection<Role> Roles => [];
}
