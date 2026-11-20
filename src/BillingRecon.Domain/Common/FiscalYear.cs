using System.Globalization;

namespace BillingRecon.Domain.Common;

/// <summary>
/// An Indian fiscal year (BR-004): 1 April of <see cref="StartYear"/> to 31 March of the year after, labelled
/// <c>2026-27</c>. A document's fiscal year comes from its business date through <see cref="FromDate"/>, never from the
/// clock: a bill dated 31 March 2027 belongs to 2026-27 whenever it is posted (ADR 0008).
/// <para>
/// A struct, like the other value objects. Its default value is not a fiscal year: <see cref="StartYear"/> is zero,
/// and everything that takes a fiscal year refuses it.
/// </para>
/// </summary>
public readonly record struct FiscalYear : IComparable<FiscalYear>
{
    /// <summary>The first fiscal year that can be named. Nothing in the system is dated before it.</summary>
    public const int MinStartYear = 1900;

    /// <summary>The last fiscal year that can be named: its end, 31 March 9999, is still a date.</summary>
    public const int MaxStartYear = 9998;

    /// <summary>The length of <see cref="Label"/>, and of the column that stores it.</summary>
    public const int LabelLength = 7;

    private const int FirstMonth = 4;

    private FiscalYear(int startYear) => StartYear = startYear;

    /// <summary>The calendar year the fiscal year starts in: 2026 for 2026-27.</summary>
    public int StartYear { get; }

    /// <summary>True for the default value of the type, which is not a fiscal year.</summary>
    public bool IsDefault => StartYear == 0;

    /// <summary>The first day: 1 April of <see cref="StartYear"/>.</summary>
    public DateOnly Start => new(StartYear, FirstMonth, 1);

    /// <summary>The last day: 31 March of the year after <see cref="StartYear"/>.</summary>
    public DateOnly End => new(StartYear + 1, FirstMonth - 1, 31);

    /// <summary>The label as it is stored and reported: <c>2026-27</c> (BR-004). Always seven characters.</summary>
    public string Label => string.Create(CultureInfo.InvariantCulture, $"{StartYear:D4}-{(StartYear + 1) % 100:D2}");

    /// <summary>The label as a document number carries it: <c>26-27</c> (ADR 0008). Always five characters.</summary>
    public string ShortLabel => string.Create(CultureInfo.InvariantCulture, $"{StartYear % 100:D2}-{(StartYear + 1) % 100:D2}");

    /// <summary>The fiscal year that starts on 1 April of <paramref name="startYear"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="startYear"/> is outside the years that can be named.</exception>
    public static FiscalYear Of(int startYear)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(startYear, MinStartYear);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startYear, MaxStartYear);

        return new FiscalYear(startYear);
    }

    /// <summary>
    /// The fiscal year <paramref name="businessDate"/> falls in (BR-004): from April the year of the date, before
    /// April the year before.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The date is before 1 April 1900.</exception>
    public static FiscalYear FromDate(DateOnly businessDate)
        => Of(businessDate.Month >= FirstMonth ? businessDate.Year : businessDate.Year - 1);

    /// <summary>The fiscal year a stored label names. A text that is not a label is a bug or corrupt data and throws.</summary>
    /// <exception cref="FormatException"><paramref name="label"/> is not a label such as <c>2026-27</c>.</exception>
    public static FiscalYear Parse(string label)
        => TryParse(label, out var fiscalYear) ? fiscalYear : throw new FormatException("A fiscal year label has the form 2026-27.");

    /// <summary>Whether <paramref name="label"/> names a fiscal year: four digits, a hyphen, and the last two digits of the next year.</summary>
    public static bool TryParse(string? label, out FiscalYear fiscalYear)
    {
        fiscalYear = default;

        if (label is not { Length: LabelLength }
            || label[4] != '-'
            || !int.TryParse(label.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var startYear)
            || !int.TryParse(label.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var endYear)
            || startYear is < MinStartYear or > MaxStartYear
            || endYear != (startYear + 1) % 100)
        {
            return false;
        }

        fiscalYear = new FiscalYear(startYear);
        return true;
    }

    public static bool operator <(FiscalYear left, FiscalYear right) => left.StartYear < right.StartYear;

    public static bool operator <=(FiscalYear left, FiscalYear right) => left.StartYear <= right.StartYear;

    public static bool operator >(FiscalYear left, FiscalYear right) => left.StartYear > right.StartYear;

    public static bool operator >=(FiscalYear left, FiscalYear right) => left.StartYear >= right.StartYear;

    /// <summary>Whether <paramref name="businessDate"/> falls in this fiscal year, both ends included.</summary>
    public bool Contains(DateOnly businessDate) => businessDate >= Start && businessDate <= End;

    /// <inheritdoc />
    public int CompareTo(FiscalYear other) => StartYear.CompareTo(other.StartYear);

    /// <summary>The label: <c>2026-27</c>.</summary>
    public override string ToString() => Label;
}
