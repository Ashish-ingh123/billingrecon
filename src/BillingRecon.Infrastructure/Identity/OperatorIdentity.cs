using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// The identity of an operator's command on one tenant (ADR 0052): it acts for that tenant, so its rows and audit records
/// carry it, and for no user, so its audit records say "no user". Made on purpose by <see cref="TenantOperations"/>; never
/// the identity of a request.
/// </summary>
internal sealed class OperatorIdentity(TenantId tenantId) : ITenantContext, ICurrentUser
{
    /// <inheritdoc />
    public TenantId? TenantId { get; } = tenantId;

    /// <inheritdoc />
    public UserId? UserId => null;

    /// <inheritdoc />
    public IReadOnlyCollection<Role> Roles => [];
}
