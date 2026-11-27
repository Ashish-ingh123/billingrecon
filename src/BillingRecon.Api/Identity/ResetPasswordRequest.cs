using System.Text;
using System.Text.Json.Serialization;
using BillingRecon.Application.Identity;

namespace BillingRecon.Api.Identity;

/// <summary>The body of <c>POST /api/v1/auth/reset-password</c>: what the link of the mail carried, and the new password.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResetPasswordRequest
{
    /// <summary>The code of the hospital, from the link.</summary>
    public string? Tenant { get; init; }

    /// <summary>The token, from the link.</summary>
    public string? Token { get; init; }

    /// <summary>The new password: 12 to 128 characters.</summary>
    public string? NewPassword { get; init; }

    /// <summary>The command of the use case.</summary>
    public ResetPasswordByLinkCommand ToCommand() => new(Tenant ?? string.Empty, Token ?? string.Empty, NewPassword ?? string.Empty);

    // A text made of the request, in a log line or an exception, carries neither the link nor the password.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Tenant = ").Append(Tenant)
            .Append(", Token = ").Append(Token is { Length: > 0 } ? "***" : string.Empty)
            .Append(", NewPassword = ").Append(NewPassword is { Length: > 0 } ? "***" : string.Empty);

        return true;
    }
}
