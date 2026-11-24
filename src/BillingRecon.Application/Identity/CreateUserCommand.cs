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

    // A text made of the command, in a log line or an exception, never carries the password (backlog B24).
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Email = ").Append(Email).Append(", DisplayName = ").Append(DisplayName)
            .Append(", Roles = ").Append(string.Join(", ", Roles));
        return true;
    }
}
