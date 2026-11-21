using System.Text.Json.Serialization;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Identity;

/// <summary>The body of <c>PATCH /api/v1/users/{id}</c>: both values, given again. Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateUserRequest
{
    /// <summary>The e-mail address. Required.</summary>
    public string? Email { get; init; }

    /// <summary>The display name. Required.</summary>
    public string? DisplayName { get; init; }

    /// <summary>The command for user <paramref name="id"/>, at the version the client read.</summary>
    public UpdateUserCommand ToCommand(UserId id, string expectedVersion) => new(id, expectedVersion, Email ?? string.Empty, DisplayName ?? string.Empty);
}
