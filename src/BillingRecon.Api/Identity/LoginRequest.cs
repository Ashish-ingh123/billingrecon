using System.Text;
using System.Text.Json.Serialization;
using BillingRecon.Application.Identity;

namespace BillingRecon.Api.Identity;

/// <summary>The body of <c>POST /api/v1/auth/login</c>: the hospital's code, the e-mail address and the password. Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LoginRequest
{
    /// <summary>The code of the tenant (the hospital), in any case.</summary>
    public string? Tenant { get; init; }

    /// <summary>The e-mail address of the user, in any case.</summary>
    public string? Email { get; init; }

    /// <summary>The password.</summary>
    public string? Password { get; init; }

    /// <summary>The sign-in this request asks for.</summary>
    public SignInCommand ToCommand() => new(Tenant ?? string.Empty, Email ?? string.Empty, Password ?? string.Empty);

    // A text made of the request, in a log line or an exception, never carries the address or the password.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Tenant = ").Append(Tenant);

        return true;
    }
}
