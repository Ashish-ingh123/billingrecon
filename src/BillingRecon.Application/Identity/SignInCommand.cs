using System.Text;

namespace BillingRecon.Application.Identity;

/// <summary>A sign-in (US-02): the code of the tenant, the e-mail address and the password.</summary>
public sealed record SignInCommand(string Tenant, string Email, string Password)
{
    // A text made of the command, in a log line or an exception, never carries the address or the password.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Tenant = ").Append(Tenant);

        return true;
    }
}
