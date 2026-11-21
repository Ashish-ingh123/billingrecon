using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>
/// One role a user holds (BR-601): a child of <see cref="User"/>, keyed by the user and the role, with who granted it
/// and when. A role taken away is deleted (<see cref="IHardDeletable"/>): the row says what is true now, and the
/// audit trail of the user keeps every change (BR-701).
/// </summary>
public sealed class UserRole : ITenantScoped, IHardDeletable
{
    // Also the constructor Entity Framework uses to load a row; it then sets the remaining properties.
    private UserRole(Role role)
    {
        Role = role;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The user who holds the role. Set by Entity Framework from the user it belongs to.</summary>
    public UserId UserId { get; private set; }

    /// <summary>The role.</summary>
    public Role Role { get; private set; }

    /// <summary>When it was granted.</summary>
    public DateTimeOffset GrantedAtUtc { get; private set; }

    /// <summary>Who granted it; null when no user did, as for the first user of a tenant.</summary>
    public UserId? GrantedByUserId { get; private set; }

    internal static UserRole Grant(Role role, DateTimeOffset grantedAtUtc, UserId? grantedBy) => new(role)
    {
        GrantedAtUtc = grantedAtUtc,
        GrantedByUserId = grantedBy,
    };
}
