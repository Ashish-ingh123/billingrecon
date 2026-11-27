using System.Text;

namespace BillingRecon.Application.Identity;

/// <summary>A new password is set with the link of the mail.</summary>
public sealed record ResetPasswordByLinkCommand(string Tenant, string Token, string NewPassword)
{
    // A text made of the command, in a log line or an exception, carries neither the link nor the password.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Tenant = ").Append(Tenant)
            .Append(", Token = ").Append(Token.Length > 0 ? "***" : string.Empty)
            .Append(", NewPassword = ").Append(NewPassword.Length > 0 ? "***" : string.Empty);

        return true;
    }
}
