using System.Text;

namespace BillingRecon.Application.Identity;

/// <summary>What an enrolment that was begun answers: the secret for the authenticator app, as text and as the address a QR code holds.</summary>
public sealed record SecondStepEnrolmentDto(string Secret, string ProvisioningUri)
{
    // The secret is one: a text made of the answer, in a log line or an exception, leaves it out.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Secret = ").Append(Secret.Length > 0 ? "***" : string.Empty);

        return true;
    }
}
