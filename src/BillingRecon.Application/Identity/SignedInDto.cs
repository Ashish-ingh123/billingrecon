using System.Globalization;
using System.Text;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>What a successful sign-in answers (docs/api.md, section 8.12): the token, its type, and when it expires.</summary>
public sealed record SignedInDto(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc, int ExpiresInSeconds, bool CredentialsChangeDue, SecondStepStage SecondStep = SecondStepStage.None)
{
    // The token is a bearer secret: a text made of the answer, in a log line or an exception, leaves it out.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("TokenType = ").Append(TokenType)
            .Append(", ExpiresAtUtc = ").Append(ExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture))
            .Append(", ExpiresInSeconds = ").Append(ExpiresInSeconds.ToString(CultureInfo.InvariantCulture))
            .Append(", CredentialsChangeDue = ").Append(CredentialsChangeDue ? "true" : "false")
            .Append(", SecondStep = ").Append(SecondStep.ToString());

        return true;
    }
}
