using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// The tenant and user of a test scope. Both can be changed, and both can be null (system work). It holds no role
/// unless a test gives it some: a context built by hand asks for none.
/// </summary>
public sealed class FixedIdentity(TenantId? tenantId, UserId? userId = null) : ITenantContext, ICurrentUser
{
    /// <inheritdoc />
    public TenantId? TenantId { get; set; } = tenantId;

    /// <inheritdoc />
    public UserId? UserId { get; set; } = userId;

    /// <inheritdoc />
    public IReadOnlyCollection<Role> Roles { get; set; } = [];

    /// <summary>A new identity with a random tenant and a random user.</summary>
    public static FixedIdentity Random() => new(new TenantId(Guid.NewGuid()), new UserId(Guid.NewGuid()));
}
