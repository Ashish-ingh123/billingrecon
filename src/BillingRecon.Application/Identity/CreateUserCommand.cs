using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>Creates a user of the current tenant with a password and its roles (US-01).</summary>
public sealed record CreateUserCommand
{
    /// <summary>The e-mail address, unique in the tenant without regard to case.</summary>
    public required string Email { get; init; }

    /// <summary>The name shown for the user.</summary>
    public required string DisplayName { get; init; }

    /// <summary>The first password, 12 to 128 characters. Hashed at once; never stored or logged.</summary>
    public required string Password { get; init; }

    /// <summary>At least one role.</summary>
    public required IReadOnlyList<Role> Roles { get; init; }
}
