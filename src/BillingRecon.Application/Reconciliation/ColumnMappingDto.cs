using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// The column mapping of a bank channel as the API takes it in and hands it out (BR-1002): the header line, the header
/// names of each column, the date format, how the money is laid out and how its decimals are written.
/// </summary>
public sealed record ColumnMappingDto
{
    /// <summary>The line of the file that holds the header, from 1.</summary>
    public int HeaderRow { get; init; } = 1;

    /// <summary>The column of the transaction date.</summary>
    public string? DateColumn { get; init; }

    /// <summary>One of the formats the API lists, such as dd/MM/yyyy.</summary>
    public string? DateFormat { get; init; }

    /// <summary>The column of the value date, when the bank gives one.</summary>
    public string? ValueDateColumn { get; init; }

    /// <summary>The column of the description.</summary>
    public string? DescriptionColumn { get; init; }

    /// <summary>The column of the reference, when the bank gives one.</summary>
    public string? ReferenceColumn { get; init; }

    /// <summary>OneColumn (a signed amount) or TwoColumns (a debit and a credit).</summary>
    public AmountLayout AmountLayout { get; init; }

    /// <summary>The signed amount's column, for OneColumn.</summary>
    public string? AmountColumn { get; init; }

    /// <summary>The debit column, for TwoColumns.</summary>
    public string? DebitColumn { get; init; }

    /// <summary>The credit column, for TwoColumns.</summary>
    public string? CreditColumn { get; init; }

    /// <summary>The column of the running balance, when the bank gives one.</summary>
    public string? BalanceColumn { get; init; }

    /// <summary>Point (1,234.56) or Comma (1.234,56).</summary>
    public DecimalStyle DecimalStyle { get; init; }

    /// <summary>The mapping the channel keeps: every name trimmed, and an empty name taken as none.</summary>
    public ColumnMapping ToMapping() => new()
    {
        HeaderRow = HeaderRow,
        DateColumn = Clean(DateColumn) ?? string.Empty,
        DateFormat = DateFormat?.Trim() ?? string.Empty,
        ValueDateColumn = Clean(ValueDateColumn),
        DescriptionColumn = Clean(DescriptionColumn) ?? string.Empty,
        ReferenceColumn = Clean(ReferenceColumn),
        AmountLayout = AmountLayout,
        AmountColumn = Clean(AmountColumn),
        DebitColumn = Clean(DebitColumn),
        CreditColumn = Clean(CreditColumn),
        BalanceColumn = Clean(BalanceColumn),
        DecimalStyle = DecimalStyle,
    };

    /// <summary>The mapping as the API hands it out.</summary>
    public static ColumnMappingDto Of(ColumnMapping mapping) => new()
    {
        HeaderRow = mapping.HeaderRow,
        DateColumn = mapping.DateColumn,
        DateFormat = mapping.DateFormat,
        ValueDateColumn = mapping.ValueDateColumn,
        DescriptionColumn = mapping.DescriptionColumn,
        ReferenceColumn = mapping.ReferenceColumn,
        AmountLayout = mapping.AmountLayout,
        AmountColumn = mapping.AmountColumn,
        DebitColumn = mapping.DebitColumn,
        CreditColumn = mapping.CreditColumn,
        BalanceColumn = mapping.BalanceColumn,
        DecimalStyle = mapping.DecimalStyle,
    };

    private static string? Clean(string? name) => string.IsNullOrWhiteSpace(name) ? null : name.Trim();
}
