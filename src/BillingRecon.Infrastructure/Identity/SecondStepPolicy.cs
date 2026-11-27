using BillingRecon.Application.Identity;
using BillingRecon.Domain.Identity;
using Microsoft.Extensions.Options;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>Who must have a second step, from <c>TwoStep:Required</c> (ADR 0061): a TenantAdmin and a Supervisor.</summary>
public sealed class SecondStepPolicy(IOptions<TwoStepOptions> options) : ISecondStepPolicy
{
    /// <inheritdoc />
    public bool IsRequired => options.Value.Required;

    /// <inheritdoc />
    public string Issuer => "BillingRecon";

    /// <inheritdoc />
    public bool IsRequiredFor(IReadOnlyCollection<Role> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        return IsRequired && (roles.Contains(Role.TenantAdmin) || roles.Contains(Role.Supervisor));
    }
}
