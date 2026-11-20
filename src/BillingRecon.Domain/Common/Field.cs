using System.Runtime.CompilerServices;

namespace BillingRecon.Domain.Common;

/// <summary>
/// The text rules every field of an aggregate shares, whatever its module: no surrounding white space, a blank optional
/// value is null, and the length fits the column. The validator of the use case checks the same limits first, so a
/// value that fails here is a bug in the caller and throws. The message names the field, never its value, which may be
/// patient data.
/// </summary>
internal static class Field
{
    /// <summary>A value the caller must supply: trimmed, 1 to <paramref name="maxLength"/> characters.</summary>
    public static string Required(string? value, int maxLength, [CallerArgumentExpression("value")] string field = "")
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > maxLength)
        {
            throw new ArgumentException(
                $"{field} must have 1 to {maxLength} characters after trimming; the validator of the use case should have refused it.",
                field);
        }

        return trimmed;
    }

    /// <summary>A value the caller may leave out: trimmed, null when blank, at most <paramref name="maxLength"/> characters.</summary>
    public static string? Optional(string? value, int maxLength, [CallerArgumentExpression("value")] string field = "")
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException(
                $"{field} must have at most {maxLength} characters after trimming; the validator of the use case should have refused it.",
                field);
        }

        return trimmed;
    }
}
