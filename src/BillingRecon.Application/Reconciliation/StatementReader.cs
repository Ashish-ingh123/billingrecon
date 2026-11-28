using System.Globalization;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// Reads a bank's statement with a channel's column mapping (BR-1002, BR-1003, BR-1005): the lines it holds and every row
/// it refuses with the reason in words. Pure: it reads text and writes nothing, so the dry run and the import read a file
/// alike. A file that cannot be read as a whole (too long, not UTF-8, no header, a missing column) is unreadable.
/// </summary>
public static class StatementReader
{
    /// <summary>
    /// The most characters a file may hold: what fits one request under the proxy's 1 MB limit (ADR 0045) with its JSON
    /// around it (BR-1003, as amended by the plan of Prompt 13.1). About 6,000 lines of a bank's statement.
    /// </summary>
    public const int MaxFileLength = 900_000;

    /// <summary>The character a browser puts for bytes that are not UTF-8: a file in another encoding holds it.</summary>
    private const char ReplacementCharacter = '�';

    /// <summary>Reads <paramref name="text"/> with <paramref name="mapping"/>; a date after <paramref name="today"/> (IST) is refused.</summary>
    public static StatementReading Read(string? text, ColumnMapping mapping, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return StatementReading.Unreadable("the file is empty.");
        }

        if (text.Length > MaxFileLength)
        {
            return StatementReading.Unreadable($"it is larger than 1 MB ({MaxFileLength:N0} characters). Send a longer statement as several files.");
        }

        if (text.Contains(ReplacementCharacter, StringComparison.Ordinal))
        {
            return StatementReading.Unreadable("it is not UTF-8 text. Open it in your spreadsheet and save it as \"CSV UTF-8\".");
        }

        var skipped = mapping.HeaderRow - 1;
        var body = SkipLines(text, skipped);
        if (body is null)
        {
            return StatementReading.Unreadable($"it has fewer than {mapping.HeaderRow} lines, so it has no header on line {mapping.HeaderRow}.");
        }

        var table = CsvTable.Parse(body);
        var required = new[]
        {
            mapping.DateColumn, mapping.ValueDateColumn, mapping.DescriptionColumn, mapping.ReferenceColumn,
            mapping.AmountColumn, mapping.DebitColumn, mapping.CreditColumn, mapping.BalanceColumn,
        }.OfType<string>().ToArray();
        var missing = table.Missing(required);
        if (missing.Count > 0)
        {
            return StatementReading.Unreadable(
                $"line {mapping.HeaderRow} has no column {string.Join(", ", missing.Select(name => $"\"{name}\""))}. Check the channel's mapping against the file's header.");
        }

        // A row of empty fields (a line of commas, as a spreadsheet writes below the data) is a blank line, not a row.
        var rows = table.Rows.Where(row => !row.Fields.All(string.IsNullOrWhiteSpace)).ToList();
        var lines = new List<ReadStatementRow>();
        var refusals = new List<StatementRefusal>();
        foreach (var row in rows)
        {
            var rowNumber = row.Line + skipped;
            var read = ReadRow(table, row, mapping, today);
            if (read.Reason is { } reason)
            {
                refusals.Add(new StatementRefusal(rowNumber, reason));
            }
            else
            {
                lines.Add(new ReadStatementRow(rowNumber, read.Values!, RawText(row)));
            }
        }

        return new StatementReading(null, rows.Count, lines, refusals);
    }

    private static (StatementLineValues? Values, string? Reason) ReadRow(CsvTable table, CsvRow row, ColumnMapping mapping, DateOnly today)
    {
        if (!TryDate(table.Field(row, mapping.DateColumn), mapping.DateFormat, out var date))
        {
            return (null, $"the date is missing or not in the form {mapping.DateFormat}.");
        }

        if (date > today)
        {
            return (null, $"the date {date:dd/MM/yyyy} is after today.");
        }

        DateOnly? valueDate = null;
        if (mapping.ValueDateColumn is { } valueDateColumn && table.Field(row, valueDateColumn) is { } valueDateText)
        {
            if (!TryDate(valueDateText, mapping.DateFormat, out var value))
            {
                return (null, $"the value date is not in the form {mapping.DateFormat}.");
            }

            valueDate = value;
        }

        var (direction, amount, moneyReason) = mapping.AmountLayout == AmountLayout.OneColumn
            ? OneColumn(table.Field(row, mapping.AmountColumn!), mapping.DecimalStyle)
            : TwoColumns(table.Field(row, mapping.DebitColumn!), table.Field(row, mapping.CreditColumn!), mapping.DecimalStyle);
        if (moneyReason is not null)
        {
            return (null, moneyReason);
        }

        Money? balance = null;
        if (mapping.BalanceColumn is { } balanceColumn && table.Field(row, balanceColumn) is { } balanceText)
        {
            var (signed, reason) = SignedAmount(balanceText, mapping.DecimalStyle);
            if (reason is not null)
            {
                return (null, $"the balance {reason}");
            }

            balance = signed;
        }

        var reference = mapping.ReferenceColumn is { } referenceColumn ? Cut(table.Field(row, referenceColumn), StatementLine.ReferenceMaxLength) : null;
        var description = Cut(table.Field(row, mapping.DescriptionColumn), StatementLine.DescriptionMaxLength) ?? reference;
        if (description is null)
        {
            return (null, "the row has neither a description nor a reference.");
        }

        return (new StatementLineValues(date, valueDate, direction, amount, reference, description, balance), null);
    }

    // One signed column: below zero, or with "Dr", is money out; above zero, or with "Cr", money in.
    private static (StatementDirection Direction, Money Amount, string? Reason) OneColumn(string? text, DecimalStyle style)
    {
        if (text is null)
        {
            return (default, default, "the amount is missing.");
        }

        StatementDirection? marked = null;
        var trimmed = text.Trim();
        if (trimmed.EndsWith("cr", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith("dr", StringComparison.OrdinalIgnoreCase))
        {
            marked = trimmed.EndsWith("cr", StringComparison.OrdinalIgnoreCase) ? StatementDirection.In : StatementDirection.Out;
            trimmed = trimmed[..^2].TrimEnd('.', ' ');
        }

        var (signed, reason) = SignedAmount(trimmed, style);
        if (reason is not null)
        {
            return (default, default, $"the amount {reason}");
        }

        if (signed == Money.Zero)
        {
            return (default, default, "the amount is zero.");
        }

        if (marked is not null && signed.IsNegative)
        {
            return (default, default, "the amount has both a minus sign and Cr or Dr.");
        }

        var direction = marked ?? (signed.IsNegative ? StatementDirection.Out : StatementDirection.In);
        return (direction, signed.IsNegative ? -signed : signed, null);
    }

    // A debit and a credit column: exactly one holds an amount above zero (a zero in the other is no amount).
    private static (StatementDirection Direction, Money Amount, string? Reason) TwoColumns(string? debit, string? credit, DecimalStyle style)
    {
        var (debitAmount, debitReason) = debit is null ? (Money.Zero, null) : SignedAmount(debit, style);
        var (creditAmount, creditReason) = credit is null ? (Money.Zero, null) : SignedAmount(credit, style);
        if (debitReason is not null || creditReason is not null)
        {
            return (default, default, $"the {(debitReason is not null ? "debit" : "credit")} {debitReason ?? creditReason}");
        }

        if (debitAmount.IsNegative || creditAmount.IsNegative)
        {
            return (default, default, "a debit or a credit is below zero.");
        }

        return (debitAmount > Money.Zero, creditAmount > Money.Zero) switch
        {
            (true, true) => (default, default, "the row has both a debit and a credit."),
            (false, false) => (default, default, "the row has neither a debit nor a credit."),
            (true, false) => (StatementDirection.Out, debitAmount, null),
            (false, true) => (StatementDirection.In, creditAmount, null),
        };
    }

    // An amount with its grouping removed, at most two decimals; in brackets or with a minus sign it is below zero.
    private static (Money Amount, string? Reason) SignedAmount(string text, DecimalStyle style)
    {
        var value = text.Trim().Replace("₹", string.Empty, StringComparison.Ordinal).Replace("INR", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        var negative = false;
        if (value.StartsWith('(') && value.EndsWith(')'))
        {
            negative = true;
            value = value[1..^1].Trim();
        }

        value = style == DecimalStyle.Point
            ? value.Replace(",", string.Empty, StringComparison.Ordinal)
            : value.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        value = value.Replace(" ", string.Empty, StringComparison.Ordinal);

        if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            return (default, $"\"{Cut(text, 30)}\" is not a number.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            return (default, "has more than two decimals.");
        }

        if (Math.Abs(amount) > 999_999_999_999.99m)
        {
            return (default, "is too large.");
        }

        return (Money.Of(negative ? -amount : amount), null);
    }

    private static bool TryDate(string? text, string format, out DateOnly date)
        => DateOnly.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date);

    private static string? Cut(string? text, int maxLength)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    // The row as the file held it, its fields joined by commas, quoted where a field holds a comma or a quote.
    private static string RawText(CsvRow row)
    {
        var joined = string.Join(',', row.Fields.Select(field =>
            field.Contains(',', StringComparison.Ordinal) || field.Contains('"', StringComparison.Ordinal) || field.Contains('\n', StringComparison.Ordinal)
                ? $"\"{field.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
                : field));

        return joined.Length <= StatementLine.RawTextMaxLength ? joined : joined[..StatementLine.RawTextMaxLength];
    }

    // The text after its first lines, or null when it has fewer lines than that.
    private static string? SkipLines(string text, int lines)
    {
        var position = 0;
        for (var skipped = 0; skipped < lines; skipped++)
        {
            var next = text.IndexOf('\n', position);
            if (next < 0)
            {
                return null;
            }

            position = next + 1;
        }

        return text[position..];
    }
}
