namespace BillingRecon.Domain.Billing;

/// <summary>
/// The code of an entry of the service catalogue: the short name a clerk types and a price list prints, such as
/// <c>CONS-OPD</c> or <c>LAB/CBC</c>. It is kept as entered, without the surrounding white space: 1 to 30 ASCII
/// characters, each a letter, a digit or one of <c>. - _ /</c>, so it fits a <c>varchar</c> column and never needs
/// quoting. Two codes that differ only in the case of their letters are the same code. Equality ignores case here, as
/// the unique index on the column does under the case-insensitive collation of the database.
/// <para>
/// A struct, like the other value objects, so that Entity Framework stores it as a column and never takes it for an
/// entity. Its default value is not a valid instance: it has an empty <see cref="Value"/>, and
/// <see cref="ServiceItem.Create"/> refuses it.
/// </para>
/// </summary>
public readonly record struct ServiceItemCode
{
    /// <summary>Longest allowed code.</summary>
    public const int MaxLength = 30;

    private readonly string? _value;

    private ServiceItemCode(string value) => _value = value;

    /// <summary>The code as entered, without surrounding white space. Empty only for the default value of the type.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>True for the default value of the type, which is not a code.</summary>
    public bool IsEmpty => Value.Length == 0;

    /// <summary>The code in <paramref name="value"/>. A value that is not one is a bug in the caller and throws.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a code.</exception>
    public static ServiceItemCode Of(string value)
        => TryOf(value, out var code)
            ? code
            : throw new ArgumentException(
                $"A service item code has 1 to {MaxLength} characters after trimming: ASCII letters, digits, '.', '-', '_' and '/'.",
                nameof(value));

    /// <summary>Whether <paramref name="value"/> is a code, for the validator that turns input into a field error.</summary>
    public static bool TryOf(string? value, out ServiceItemCode code)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength || !trimmed.All(IsAllowed))
        {
            code = default;
            return false;
        }

        code = new ServiceItemCode(trimmed);
        return true;
    }

    /// <summary>Two codes are equal when their text is, ignoring the case of letters.</summary>
    public bool Equals(ServiceItemCode other) => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    /// <summary>The code as entered.</summary>
    public override string ToString() => Value;

    private static bool IsAllowed(char character) => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or '/';
}
