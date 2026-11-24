using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>An administrator sets a new password for a user, under the version the client read.</summary>
public sealed record ResetUserPasswordCommand(UserId Id, string ExpectedVersion, string Password)
{
    // A text made of the command, in a log line or an exception, never carries the password (backlog B24).
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Id = ").Append(Id).Append(", ExpectedVersion = ").Append(ExpectedVersion);
        return true;
    }
}
