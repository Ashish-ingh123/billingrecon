using System.Text.Json.Serialization;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Api.Identity;

/// <summary>The body of <c>PUT /api/v1/users/{id}/roles</c>: every role the user is to hold. Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReplaceUserRolesRequest
{
    /// <summary>At least one role.</summary>
    public IReadOnlyList<Role>? Roles { get; init; }

    /// <summary>The command for user <paramref name="id"/>, at the version the client read.</summary>
    public ReplaceUserRolesCommand ToCommand(UserId id, string expectedVersion) => new(id, expectedVersion, Roles ?? []);
}
