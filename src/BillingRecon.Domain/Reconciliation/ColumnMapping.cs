namespace BillingRecon.Domain.Reconciliation;

/// <summary>
/// How a bank's CSV is read (BR-1002; ADR 0065): the line that holds the header, the columns of each value by their
/// header names, the date format and the decimal style. Saved per bank channel, as JSON in one column. A struct, so
/// that Entity Framework never takes it for an entity.
/// </summary>
public readonly record struct ColumnMapping
{
    /// <summary>The largest <see cref="HeaderRow"/>: a bank writes a few lines about the account above the header.</summary>
    public const int MaxHeaderRow = 50;

    /// <summary>Longest allowed name of a column.</summary>
    public const int ColumnMaxLength = 100;

    /// <summary>The date formats a mapping may name, as .NET writes them; each is read exactly, in the invariant culture.</summary>
    public static IReadOnlyList<string> DateFormats { get; } =
        ["dd/MM/yyyy", "dd-MM-yyyy", "dd.MM.yyyy", "dd/MM/yy", "dd-MM-yy", "dd-MMM-yyyy", "dd-MMM-yy", "dd MMM yyyy", "yyyy-MM-dd"];

    /// <summary>The line of the file that holds the header, from 1; the lines above it are skipped.</summary>
    public int HeaderRow { get; init; }

    /// <summary>The column of the transaction date. Required.</summary>
    public string DateColumn { get; init; }

    /// <summary>One of <see cref="DateFormats"/>, for both dates.</summary>
    public string DateFormat { get; init; }

    /// <summary>The column of the value date, when the bank gives one.</summary>
    public string? ValueDateColumn { get; init; }

    /// <summary>The column of the description (the narration). Required.</summary>
    public string DescriptionColumn { get; init; }

    /// <summary>The column of the reference (UTR, cheque number); when absent, the description is the only text.</summary>
    public string? ReferenceColumn { get; init; }

    /// <summary>Whether the money is one signed column or a debit and a credit column.</summary>
    public AmountLayout AmountLayout { get; init; }

    /// <summary>The signed amount's column, for <see cref="AmountLayout.OneColumn"/>.</summary>
    public string? AmountColumn { get; init; }

    /// <summary>The debit (money out) column, for <see cref="AmountLayout.TwoColumns"/>.</summary>
    public string? DebitColumn { get; init; }

    /// <summary>The credit (money in) column, for <see cref="AmountLayout.TwoColumns"/>.</summary>
    public string? CreditColumn { get; init; }

    /// <summary>The column of the running balance, when the bank gives one.</summary>
    public string? BalanceColumn { get; init; }

    /// <summary>How the amounts write their decimals.</summary>
    public DecimalStyle DecimalStyle { get; init; }

    /// <summary>
    /// What is wrong with <paramref name="mapping"/> in words, or null when it can be used. The validator of a use case
    /// asks it first; the channel refuses a mapping it names.
    /// </summary>
    public static string? Problem(ColumnMapping mapping)
    {
        if (mapping.HeaderRow is < 1 or > MaxHeaderRow)
        {
            return $"The header row is a line from 1 to {MaxHeaderRow}.";
        }

        if (!IsName(mapping.DateColumn) || !IsName(mapping.DescriptionColumn))
        {
            return $"The date and the description columns are required, each 1 to {ColumnMaxLength} characters.";
        }

        if (!DateFormats.Contains(mapping.DateFormat, StringComparer.Ordinal))
        {
            return $"The date format is one of {string.Join(", ", DateFormats)}.";
        }

        if (!Enum.IsDefined(mapping.AmountLayout) || !Enum.IsDefined(mapping.DecimalStyle))
        {
            return "The amount layout is OneColumn or TwoColumns, and the decimal style Point or Comma.";
        }

        var money = mapping.AmountLayout == AmountLayout.OneColumn
            ? IsName(mapping.AmountColumn) && mapping.DebitColumn is null && mapping.CreditColumn is null
            : IsName(mapping.DebitColumn) && IsName(mapping.CreditColumn) && mapping.AmountColumn is null;
        if (!money)
        {
            return "A one-column layout names the amount column only; a two-column layout names the debit and the credit columns only.";
        }

        foreach (var optional in new[] { mapping.ValueDateColumn, mapping.ReferenceColumn, mapping.BalanceColumn })
        {
            if (optional is not null && !IsName(optional))
            {
                return $"A column is named by 1 to {ColumnMaxLength} characters.";
            }
        }

        var named = new[]
        {
            mapping.DateColumn, mapping.ValueDateColumn, mapping.DescriptionColumn, mapping.ReferenceColumn,
            mapping.AmountColumn, mapping.DebitColumn, mapping.CreditColumn, mapping.BalanceColumn,
        }.OfType<string>().Select(name => name.Trim()).ToArray();

        return named.Distinct(StringComparer.OrdinalIgnoreCase).Count() == named.Length ? null : "Each value has a column of its own.";
    }

    private static bool IsName(string? name) => name is not null && name.Trim().Length is > 0 and <= ColumnMaxLength && name.Trim() == name;
}
