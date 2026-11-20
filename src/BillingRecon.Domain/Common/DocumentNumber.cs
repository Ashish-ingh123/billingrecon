using System.Globalization;

namespace BillingRecon.Domain.Common;

/// <summary>
/// The number of a posted document (BR-801, ADR 0008): <c>PREFIX/YY-YY/NNNNNN</c>, for example
/// <c>BIL/26-27/000123</c>. The prefix has one to three capital letters or digits, the middle is the short label of
/// the fiscal year, and the sequence is padded with zeros to six digits. A number is at most
/// <see cref="MaxLength"/> characters, as CGST rule 46(b) requires of a tax invoice; one that would be longer cannot
/// be built, so it can never be issued.
/// <para>
/// Only the number series creates one, inside the transaction of a posting. Nothing else has a sequence to give.
/// </para>
/// </summary>
public sealed record DocumentNumber
{
    /// <summary>The longest a number may be (CGST rule 46(b)).</summary>
    public const int MaxLength = 16;

    /// <summary>The longest a prefix may be (BR-802).</summary>
    public const int PrefixMaxLength = 3;

    private const int SequenceDigits = 6;

    private DocumentNumber(DocumentType type, FiscalYear fiscalYear, string prefix, int sequence, string value)
    {
        Type = type;
        FiscalYear = fiscalYear;
        Prefix = prefix;
        Sequence = sequence;
        Value = value;
    }

    /// <summary>The kind of document the number belongs to.</summary>
    public DocumentType Type { get; }

    /// <summary>The fiscal year of the series the number was taken from.</summary>
    public FiscalYear FiscalYear { get; }

    /// <summary>The prefix the series had when the number was taken.</summary>
    public string Prefix { get; }

    /// <summary>The position in the series, from 1, with no gap (BR-801).</summary>
    public int Sequence { get; }

    /// <summary>The number as it is stored and printed: <c>BIL/26-27/000123</c>.</summary>
    public string Value { get; }

    /// <summary>The number at position <paramref name="sequence"/> of the series of <paramref name="type"/> in <paramref name="fiscalYear"/>.</summary>
    /// <exception cref="ArgumentException">
    /// The type is not one the enum defines, the fiscal year is the default value, the prefix is not one to three
    /// capital letters or digits, or the number would be longer than <see cref="MaxLength"/> characters.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sequence"/> is less than 1.</exception>
    public static DocumentNumber Create(DocumentType type, FiscalYear fiscalYear, string prefix, int sequence)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentException("The document type is not one the enum defines.", nameof(type));
        }

        if (fiscalYear.IsDefault)
        {
            throw new ArgumentException("A document number needs a fiscal year; the default value is not one.", nameof(fiscalYear));
        }

        if (!IsPrefix(prefix))
        {
            throw new ArgumentException($"A prefix has 1 to {PrefixMaxLength} characters, each a capital letter or a digit (BR-802).", nameof(prefix));
        }

        var value = string.Create(CultureInfo.InvariantCulture, $"{prefix}/{fiscalYear.ShortLabel}/{sequence.ToString("D" + SequenceDigits, CultureInfo.InvariantCulture)}");
        if (value.Length > MaxLength)
        {
            throw new ArgumentException(
                $"Number {sequence} of the series {prefix}/{fiscalYear.ShortLabel} would have {value.Length} characters; a document number has at most {MaxLength} (CGST rule 46(b), ADR 0008).",
                nameof(sequence));
        }

        return new DocumentNumber(type, fiscalYear, prefix, sequence, value);
    }

    /// <summary>Whether <paramref name="prefix"/> is one to three characters, each a capital ASCII letter or a digit (BR-802).</summary>
    public static bool IsPrefix(string? prefix)
        => prefix is { Length: >= 1 and <= PrefixMaxLength } && prefix.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character));

    /// <summary>The number as it is stored and printed.</summary>
    public override string ToString() => Value;
}
