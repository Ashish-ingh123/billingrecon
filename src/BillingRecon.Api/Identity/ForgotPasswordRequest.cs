using System.Text.Json.Serialization;
using BillingRecon.Application.Identity;

namespace BillingRecon.Api.Identity;

/// <summary>The body of <c>POST /api/v1/auth/forgot-password</c>: the hospital's code and the address, as at the sign-in.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ForgotPasswordRequest
{
    /// <summary>The code of the hospital.</summary>
    public string? Tenant { get; init; }

    /// <summary>The e-mail address the user signs in with.</summary>
    public string? Email { get; init; }

    /// <summary>The command of the use case.</summary>
    public RequestPasswordResetCommand ToCommand() => new(Tenant ?? string.Empty, Email ?? string.Empty);
}
