using BillingRecon.Application.Common;
using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>The rules the validators of the user use cases share (BR-601; ADR 0030).</summary>
internal static class UserRules
{
    /// <summary>Shortest allowed password (NIST SP 800-63B: length, not composition).</summary>
    public const int PasswordMinLength = 12;

    /// <summary>Longest allowed password: long passphrases are welcome, unbounded input is not.</summary>
    public const int PasswordMaxLength = 128;

    /// <summary>A required address: one @, a domain with a dot, no white space, at most 256 characters.</summary>
    public static IRuleBuilderOptions<T, string> UserEmail<T>(this IRuleBuilder<T, string> rule)
        => rule.Must(value => value is { } email && email.Trim() is { Length: > 0 and <= User.EmailMaxLength } trimmed && IsEmailAddress(trimmed))
            .WithMessage($"The e-mail address is required: a valid address of at most {User.EmailMaxLength} characters.");

    /// <summary>A required display name of at most 200 characters.</summary>
    public static IRuleBuilderOptions<T, string> UserDisplayName<T>(this IRuleBuilder<T, string> rule)
        => rule.RequiredText(User.DisplayNameMaxLength, "display name");

    /// <summary>12 to 128 characters, any characters; no rules of composition.</summary>
    public static IRuleBuilderOptions<T, string> UserPassword<T>(this IRuleBuilder<T, string> rule)
        => rule.Must(password => password is { Length: >= PasswordMinLength and <= PasswordMaxLength })
            .WithMessage($"The password is required: {PasswordMinLength} to {PasswordMaxLength} characters.");

    /// <summary>At least one role, each one the enum defines.</summary>
    public static IRuleBuilderOptions<T, IReadOnlyList<Role>> UserRoles<T>(this IRuleBuilder<T, IReadOnlyList<Role>> rule)
        => rule.Must(roles => roles is { Count: > 0 } && roles.All(Enum.IsDefined))
            .WithMessage("At least one role is required: TenantAdmin, Supervisor, BillingClerk, Cashier or Viewer.");

    /// <summary>The version the client read, from <c>If-Match</c>.</summary>
    public static IRuleBuilderOptions<T, string> UserVersion<T>(this IRuleBuilder<T, string> rule)
        => rule.NotEmpty().WithMessage("The version you read is required (If-Match).");

    private static bool IsEmailAddress(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at != email.LastIndexOf('@') || email.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            return false;
        }

        var domain = email[(at + 1)..];

        return domain.Length >= 3 && domain.Contains('.', StringComparison.Ordinal) && domain[0] != '.' && domain[^1] != '.';
    }
}
