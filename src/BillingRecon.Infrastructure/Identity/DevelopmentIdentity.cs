using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using Microsoft.Extensions.Options;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// A stand-in for authentication: every request acts for one fixed tenant as one fixed user, both taken from
/// configuration. It is registered in the Development environment only and goes away when the tenant and user come
/// from the validated token (Phase 5, BR-602).
/// </summary>
public sealed class DevelopmentIdentity(IOptions<DevelopmentIdentityOptions> options) : ITenantContext, ICurrentUser
{
    /// <inheritdoc />
    public TenantId? TenantId { get; } = new TenantId(options.Value.TenantId);

    /// <inheritdoc />
    public UserId? UserId { get; } = new UserId(options.Value.UserId);
}
