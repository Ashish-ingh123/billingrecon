using System.Text;

namespace BillingRecon.Application.Identity;

/// <summary>The signed-in user sets a password of their own, giving the current one (ADR 0051).</summary>
public sealed record ChangeOwnPasswordCommand(string CurrentPassword, string NewPassword)
{
    // A text made of the command, in a log line or an exception, never carries either password.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("CurrentPassword = ").Append(CurrentPassword.Length > 0 ? "***" : string.Empty)
            .Append(", NewPassword = ").Append(NewPassword.Length > 0 ? "***" : string.Empty);
        return true;
    }
}
