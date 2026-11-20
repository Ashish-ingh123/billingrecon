using FluentValidation;

namespace BillingRecon.Application.Common;

/// <summary>
/// The text rules the validators of every module share. Each looks at a value as the aggregate will store it: without
/// the surrounding white space, and a blank optional value as absent. The limits passed in are the aggregates' own
/// constants, so the validator and the column can never disagree.
/// </summary>
internal static class TextRules
{
    /// <summary>A text the client must send: 1 to <paramref name="maxLength"/> characters once trimmed.</summary>
    public static IRuleBuilderOptions<T, string> RequiredText<T>(this IRuleBuilder<T, string> rule, int maxLength, string label)
        => rule.Must(value =>
            {
                var length = Trimmed(value).Length;

                return length > 0 && length <= maxLength;
            })
            .WithMessage($"The {label} is required: 1 to {maxLength} characters.");

    /// <summary>A text the client may leave out: at most <paramref name="maxLength"/> characters once trimmed.</summary>
    public static IRuleBuilderOptions<T, string?> OptionalText<T>(this IRuleBuilder<T, string?> rule, int maxLength, string label)
        => rule.Must(value => Trimmed(value).Length <= maxLength)
            .WithMessage($"The {label} has at most {maxLength} characters.");

    private static string Trimmed(string? value) => value?.Trim() ?? string.Empty;
}
