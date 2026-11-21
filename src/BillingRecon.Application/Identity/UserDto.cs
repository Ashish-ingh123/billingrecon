using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>A user as the API answers it (docs/api.md, section 8.11): never the password or its hash.</summary>
public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    UserStatus Status,
    IReadOnlyList<Role> Roles,
    DateTimeOffset? LastSignInAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Version);
