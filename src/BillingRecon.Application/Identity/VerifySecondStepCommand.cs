using System.Text;

namespace BillingRecon.Application.Identity;

/// <summary>The code of the second step: six digits of the authenticator app, or a recovery code.</summary>
public sealed record VerifySecondStepCommand(string? Code)
{
    // A code is a secret while it is good: a text made of the command leaves it out.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Code = ").Append(Code is { Length: > 0 } ? "***" : string.Empty);

        return true;
    }
}
