using BillingRecon.Application.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>Maps a user to what the API answers.</summary>
public static class UsersMapping
{
    /// <summary>The user as a <see cref="UserDto"/>, the roles in the order of the enum, the row version as text.</summary>
    public static UserDto ToDto(this User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return new UserDto(
            Id: user.Id.Value,
            Email: user.Email,
            DisplayName: user.DisplayName,
            Status: user.Status,
            Roles: [.. user.Roles.Select(role => role.Role).Order()],
            CredentialsChangeDue: user.PasswordChangeDue,
            SecondStepEnrolled: user.SecondStepEnrolled,
            ApprovalHoldUntilUtc: user.ApprovalHoldUntilUtc,
            LastSignInAtUtc: user.LastSignInAtUtc,
            CreatedAtUtc: user.CreatedAtUtc,
            UpdatedAtUtc: user.UpdatedAtUtc,
            Version: RowVersionText.Of(user));
    }
}
