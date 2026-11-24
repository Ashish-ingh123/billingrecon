using System.Text.Json.Serialization;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Identity;

/// <summary>The body of <c>POST /api/v1/users/{id}/password</c>: the new password. Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResetUserPasswordRequest
{
    /// <summary>12 to 128 characters.</summary>
    public string? Password { get; init; }

    // A text made of the request, in a log line or an exception, never carries the password (backlog B24).
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Password = ").Append(Password is { Length: > 0 } ? "***" : string.Empty);
        return true;
    }

    /// <summary>The command for user <paramref name="id"/>, at the version the client read.</summary>
    public ResetUserPasswordCommand ToCommand(UserId id, string expectedVersion) => new(id, expectedVersion, Password ?? string.Empty);
}
