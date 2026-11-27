using System.Text;

namespace BillingRecon.Application.Identity;

/// <summary>What a confirmed enrolment answers: the recovery codes, shown this once, and the sign-in of the new session.</summary>
public sealed record SecondStepEnrolledDto(IReadOnlyList<string> RecoveryCodes, SignedInDto SignedIn)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("RecoveryCodes = ").Append(RecoveryCodes.Count > 0 ? "***" : string.Empty);

        return true;
    }
}
