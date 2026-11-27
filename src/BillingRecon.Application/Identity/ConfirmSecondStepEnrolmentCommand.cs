using System.Text;

namespace BillingRecon.Application.Identity;

/// <summary>Confirms the enrolment with the first code of the authenticator app.</summary>
public sealed record ConfirmSecondStepEnrolmentCommand(string? Code)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Code = ").Append(Code is { Length: > 0 } ? "***" : string.Empty);

        return true;
    }
}
