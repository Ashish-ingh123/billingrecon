using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>The shape of the tenant's settings and of a prefix (BR-205, BR-207, BR-802; ADR 0033), for the validators.</summary>
internal static class SettingsRules
{
    /// <summary>The highest threshold the column holds: sixteen digits and the paisa.</summary>
    public const decimal MaxThreshold = 9_999_999_999_999_999.99m;

    /// <summary>A prefix as the client typed it, trimmed and upper-cased: <c>inv</c> is <c>INV</c>.</summary>
    public static string? NormalizePrefix(string? prefix) => prefix?.Trim().ToUpperInvariant();

    public static IRuleBuilderOptions<T, string> ExpectedVersion<T>(this IRuleBuilder<T, string> rule)
        => rule.NotEmpty().WithMessage("The version you read is required (If-Match).");

    public static IRuleBuilderOptions<T, decimal?> Threshold<T>(this IRuleBuilder<T, decimal?> rule)
        => rule.Must(threshold => threshold is not { } value || (value > 0m && value <= MaxThreshold && Money.TryOf(value, out _)))
            .WithMessage($"The threshold is left out or null for none, or an amount above 0 with at most {Money.Scale} decimals.");

    public static IRuleBuilderOptions<T, int?> Days<T>(this IRuleBuilder<T, int?> rule, string label)
        => rule.NotNull().WithMessage($"The {label} is required, 0 to {Tenant.MaxDays} days.")
            .InclusiveBetween(0, Tenant.MaxDays).WithMessage($"The {label} is 0 to {Tenant.MaxDays} days.");

    public static IRuleBuilderOptions<T, string?> Prefix<T>(this IRuleBuilder<T, string?> rule)
        => rule.Must(prefix => DocumentNumber.IsPrefix(NormalizePrefix(prefix)))
            .WithMessage($"The prefix has 1 to {DocumentNumber.PrefixMaxLength} characters, each a letter or a digit (BR-802).");
}
