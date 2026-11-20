namespace BillingRecon.Domain.Payers;

/// <summary>
/// A medical record number (BR-101): the hospital's own identifier of a patient, entered from its HIS, which remains
/// the system of record. BillingRecon never generates one. It is kept as entered, without the surrounding white space:
/// 1 to 30 characters, none of them a control character. Two MRNs that differ only in the case of their letters are
/// the same MRN. Equality ignores case here, as the unique index on the column does under the case-insensitive
/// collation of the database.
/// <para>
/// A struct, like the other value objects, so that Entity Framework stores it as a column and never takes it for an
/// entity. Like <see cref="Common.Quantity"/>, its default value is not a valid instance: it has an empty
/// <see cref="Value"/>, and <see cref="Patient.Register"/> refuses it.
/// </para>
/// </summary>
public readonly record struct Mrn
{
    /// <summary>Longest allowed MRN.</summary>
    public const int MaxLength = 30;

    private readonly string? _value;

    private Mrn(string value) => _value = value;

    /// <summary>The MRN as entered, without surrounding white space. Empty only for the default value of the type.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>True for the default value of the type, which is not an MRN.</summary>
    public bool IsEmpty => Value.Length == 0;

    /// <summary>The MRN in <paramref name="value"/>. A value that is not one is a bug in the caller and throws.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not an MRN.</exception>
    public static Mrn Of(string value)
        => TryOf(value, out var mrn)
            ? mrn
            : throw new ArgumentException(
                $"An MRN has 1 to {MaxLength} characters after trimming, none of them a control character.",
                nameof(value));

    /// <summary>Whether <paramref name="value"/> is an MRN, for the validator that turns input into a field error.</summary>
    public static bool TryOf(string? value, out Mrn mrn)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength || trimmed.Any(char.IsControl))
        {
            mrn = default;
            return false;
        }

        mrn = new Mrn(trimmed);
        return true;
    }

    /// <summary>Two MRNs are equal when their text is, ignoring the case of letters.</summary>
    public bool Equals(Mrn other) => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    /// <summary>The MRN as entered.</summary>
    public override string ToString() => Value;
}
