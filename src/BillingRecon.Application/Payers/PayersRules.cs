using FluentValidation;

namespace BillingRecon.Application.Payers;

/// <summary>
/// The field rules the validators of the Payers module share, beyond the text rules of <c>TextRules</c> (Common). Each
/// looks at a value as the aggregate will store it: without the surrounding white space, and a blank optional value as
/// absent. The limits are the aggregates' own constants, so the validator and the column can never disagree.
/// </summary>
internal static class PayersRules
{
    private const int MinPhoneDigits = 7;

    // E.164: a phone number has at most fifteen digits, country code included.
    private const int MaxPhoneDigits = 15;

    /// <summary>An optional phone number: 7 to 15 digits, written with spaces, hyphens, parentheses and a leading +.</summary>
    public static IRuleBuilderOptions<T, string?> ContactPhone<T>(this IRuleBuilder<T, string?> rule, int maxLength)
        => rule.Must(value =>
            {
                var phone = Trimmed(value);

                return phone.Length == 0 || (phone.Length <= maxLength && IsPhoneNumber(phone));
            })
            .WithMessage(
                $"The phone number has {MinPhoneDigits} to {MaxPhoneDigits} digits and at most {maxLength} characters: digits, spaces, hyphens, parentheses and a leading +.");

    /// <summary>An optional e-mail address: one @, a domain with a dot, no white space.</summary>
    public static IRuleBuilderOptions<T, string?> ContactEmail<T>(this IRuleBuilder<T, string?> rule, int maxLength)
        => rule.Must(value =>
            {
                var email = Trimmed(value);

                return email.Length == 0 || (email.Length <= maxLength && IsEmailAddress(email));
            })
            .WithMessage($"The e-mail address is a valid address of at most {maxLength} characters.");

    /// <summary>
    /// An optional postal code: the six digits of an Indian PIN code, or a foreign postal code of letters, digits,
    /// spaces and hyphens. ASCII only, because the column is varchar.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> PostalCode<T>(this IRuleBuilder<T, string?> rule, int maxLength)
        => rule.Must(value =>
            {
                var code = Trimmed(value);

                return code.Length == 0
                    || (code.Length <= maxLength
                        && code.Any(char.IsAsciiLetterOrDigit)
                        && code.All(character => char.IsAsciiLetterOrDigit(character) || character is ' ' or '-'));
            })
            .WithMessage($"The PIN code has at most {maxLength} characters: letters, digits, spaces and hyphens.");

    private static string Trimmed(string? value) => value?.Trim() ?? string.Empty;

    private static bool IsPhoneNumber(string phone)
    {
        var digits = 0;
        for (var index = 0; index < phone.Length; index++)
        {
            var character = phone[index];
            if (char.IsAsciiDigit(character))
            {
                digits++;
            }
            else if (!(character == '+' && index == 0) && character is not (' ' or '-' or '(' or ')'))
            {
                return false;
            }
        }

        return digits is >= MinPhoneDigits and <= MaxPhoneDigits;
    }

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
