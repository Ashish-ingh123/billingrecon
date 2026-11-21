using System.Text.Json.Serialization;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Api.Identity;

/// <summary>The body of <c>POST /api/v1/users</c>. Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateUserRequest
{
    /// <summary>The e-mail address, unique in the tenant without regard to case; at most 256 characters.</summary>
    public string? Email { get; init; }

    /// <summary>The name shown for the user; at most 200 characters.</summary>
    public string? DisplayName { get; init; }

    /// <summary>The first password, 12 to 128 characters.</summary>
    public string? Password { get; init; }

    /// <summary>At least one of <c>TenantAdmin</c>, <c>Supervisor</c>, <c>BillingClerk</c>, <c>Cashier</c>, <c>Viewer</c>.</summary>
    public IReadOnlyList<Role>? Roles { get; init; }

    /// <summary>The command the request stands for.</summary>
    public CreateUserCommand ToCommand() => new()
    {
        Email = Email ?? string.Empty,
        DisplayName = DisplayName ?? string.Empty,
        Password = Password ?? string.Empty,
        Roles = Roles ?? [],
    };
}
