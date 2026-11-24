using System.Text.Json.Serialization;
using BillingRecon.Application.Identity;

namespace BillingRecon.Api.Identity;

/// <summary>The body of <c>POST /api/v1/auth/password</c>: the current password and the new one (ADR 0051).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChangePasswordRequest
{
    /// <summary>The password the user has now.</summary>
    public string? CurrentPassword { get; init; }

    /// <summary>The password the user chooses: 12 to 128 characters, not the current one.</summary>
    public string? NewPassword { get; init; }

    // A text made of the request, in a log line or an exception, never carries either password.
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("CurrentPassword = ").Append(CurrentPassword is { Length: > 0 } ? "***" : string.Empty)
            .Append(", NewPassword = ").Append(NewPassword is { Length: > 0 } ? "***" : string.Empty);
        return true;
    }

    /// <summary>The command of the request.</summary>
    public ChangeOwnPasswordCommand ToCommand() => new(CurrentPassword ?? string.Empty, NewPassword ?? string.Empty);
}
