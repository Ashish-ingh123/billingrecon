using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using Microsoft.Extensions.Options;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// The development tenant and user, from configuration (ADR 0015, ADR 0031 point 9). It is never the identity of a
/// request: there the Api's development scheme gives a request without a token a principal for the same two ids. It
/// is the identity of the context <see cref="DevelopmentTenantSeeder"/> writes their rows with, since no request runs
/// then. Development only.
/// </summary>
public sealed class DevelopmentIdentity(IOptions<DevelopmentIdentityOptions> options) : ITenantContext, ICurrentUser
{
    /// <inheritdoc />
    public TenantId? TenantId { get; } = new TenantId(options.Value.TenantId);

    /// <inheritdoc />
    public UserId? UserId { get; } = new UserId(options.Value.UserId);

    /// <summary>The development user is a tenant administrator, as its row says (ADR 0030).</summary>
    public IReadOnlyCollection<Role> Roles { get; } = [Role.TenantAdmin];
}
