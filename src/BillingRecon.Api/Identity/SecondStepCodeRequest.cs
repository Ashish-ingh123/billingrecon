using System.Text.Json.Serialization;

namespace BillingRecon.Api.Identity;

/// <summary>The body of the second step of a sign-in and of the confirmation of its enrolment: the code.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SecondStepCodeRequest
{
    /// <summary>Six digits of the authenticator app; for the second step of a sign-in also a recovery code.</summary>
    public string? Code { get; init; }

    // A code is a secret while it is good: a text made of the request leaves it out.
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Code = ").Append(Code is { Length: > 0 } ? "***" : string.Empty);

        return true;
    }
}
