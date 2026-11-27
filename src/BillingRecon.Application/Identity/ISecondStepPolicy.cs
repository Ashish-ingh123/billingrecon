using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>
/// Who must have a second step (ADR 0061): a TenantAdmin and a Supervisor, everywhere but in the Development and Demo
/// environments when the setting <c>TwoStep:Required</c> switches it off there. Everyone else may have one.
/// </summary>
public interface ISecondStepPolicy
{
    /// <summary>True when the requirement is in force in this environment.</summary>
    public bool IsRequired { get; }

    /// <summary>The name an authenticator app shows for the account: the product's.</summary>
    public string Issuer { get; }

    /// <summary>True when a user holding <paramref name="roles"/> must have a second step here.</summary>
    public bool IsRequiredFor(IReadOnlyCollection<Role> roles);
}
