using System.Text.RegularExpressions;

namespace BillingRecon.Infrastructure.Common.Logging;

/// <summary>
/// The PII-masking policy for logs (docs/adr/0012-structured-logging-correlation-and-pii-masking.md): e-mail addresses
/// and Indian phone numbers are masked wherever they appear in a logged string. An e-mail keeps its first character
/// and its domain; a phone number keeps its last four digits.
/// </summary>
public static partial class PiiMasker
{
    /// <summary>Stands in for a value that must not be logged and has no recognisable shape worth keeping.</summary>
    public const string FullMask = "***";

    private const int MatchTimeoutMilliseconds = 250;
    private const int PhoneDigits = 10;
    private const int PhoneDigitsKept = 4;

    private static readonly string[] SensitiveNameFragments = ["email", "phone", "mobile"];

    /// <summary>True when a property name says its value is contact data, for example <c>Email</c>, <c>PhoneNumber</c> or <c>guarantorMobile</c>.</summary>
    public static bool IsSensitiveName(string propertyName)
    {
        foreach (var fragment in SensitiveNameFragments)
        {
            if (propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Masks every e-mail address and phone number inside <paramref name="value"/>. Returns the same instance when
    /// there is nothing to mask. A GUID is never touched, on its own or inside a longer string such as a request path,
    /// so correlation, tenant, trace and entity ids stay searchable.
    /// </summary>
    public static string Mask(string value)
    {
        if (value.Length == 0 || Guid.TryParse(value, out _))
        {
            return value;
        }

        try
        {
            var masked = value;
            if (MayHoldEmail(masked))
            {
                masked = EmailPattern().Replace(masked, MaskEmail);
            }

            if (CountDigits(masked) >= PhoneDigits && MayHoldPhone(masked))
            {
                masked = PhonePattern().Replace(masked, MaskPhone);
            }

            return masked;
        }
        catch (RegexMatchTimeoutException)
        {
            // Never let a pathological string through unmasked or stall logging: drop the value instead.
            return FullMask;
        }
    }

    /// <summary>
    /// Masks a value logged under a sensitive name. A recognisable e-mail or phone number keeps its partial shape;
    /// anything else is replaced entirely, because the name already says it is personal data.
    /// </summary>
    public static string MaskSensitive(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        var masked = Mask(value);

        return ReferenceEquals(masked, value) ? FullMask : masked;
    }

    // local@domain.tld: the look-behind stops a match from starting in the middle of a longer token.
    [GeneratedRegex(
        @"(?<![A-Za-z0-9._%+-])(?<first>[A-Za-z0-9._%+-])[A-Za-z0-9._%+-]*@(?<domain>[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+)",
        RegexOptions.CultureInvariant,
        MatchTimeoutMilliseconds)]
    private static partial Regex EmailPattern();

    // Indian mobile numbers: ten digits starting 6-9, optionally written as two groups of five, optionally prefixed
    // with +91, 91, 0091 or 0. The look-arounds keep it from matching inside a longer number or an identifier, and
    // the second look-behind keeps it off the last group of a GUID inside a longer string (an id in a request path).
    [GeneratedRegex(
        @"(?<![A-Za-z0-9])(?<![0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-)(?:(?:\+|00)?91[\s-]?|0)?[6-9]\d{4}[\s-]?\d{5}(?![A-Za-z0-9])",
        RegexOptions.CultureInvariant,
        MatchTimeoutMilliseconds)]
    private static partial Regex PhonePattern();

    private static string MaskEmail(Match match) => $"{match.Groups["first"].Value}{FullMask}@{match.Groups["domain"].Value}";

    private static string MaskPhone(Match match)
    {
        Span<char> kept = stackalloc char[PhoneDigitsKept];
        var position = PhoneDigitsKept;
        for (var index = match.Length - 1; index >= 0 && position > 0; index--)
        {
            var character = match.ValueSpan[index];
            if (char.IsAsciiDigit(character))
            {
                kept[--position] = character;
            }
        }

        return string.Concat("******", kept);
    }

    // An address has a dot somewhere after its @ (the domain), so a value with no dot after its first @ holds none and
    // is not given to the pattern at all. This only spares the scan: what the pattern matches is unchanged.
    private static bool MayHoldEmail(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);

        return at >= 0 && value.IndexOf('.', at + 1) >= 0;
    }

    // A number as the pattern writes it is 10 to 16 characters long (ten digits; at most "0091", a separator, and a
    // separator between the two groups of five), with no ASCII letter or digit just before it or just after it. A value
    // with no such stretch, a very long run of digits for one, holds none and is not given to the pattern at all. This
    // only spares the scan: what the pattern matches is unchanged.
    private static bool MayHoldPhone(string value)
    {
        const int Shortest = 10;
        const int Longest = 16;

        for (var start = 0; start + Shortest <= value.Length; start++)
        {
            if (start > 0 && char.IsAsciiLetterOrDigit(value[start - 1]))
            {
                continue;
            }

            var last = Math.Min(start + Longest, value.Length);
            for (var end = start + Shortest; end <= last; end++)
            {
                if (end == value.Length || !char.IsAsciiLetterOrDigit(value[end]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int CountDigits(string value)
    {
        var digits = 0;
        foreach (var character in value)
        {
            if (char.IsAsciiDigit(character))
            {
                digits++;
            }
        }

        return digits;
    }
}
