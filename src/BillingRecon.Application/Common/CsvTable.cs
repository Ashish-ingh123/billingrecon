using System.Text;

namespace BillingRecon.Application.Common;

/// <summary>
/// A CSV file read into rows (ADR 0053): UTF-8 text, a comma between fields, the first line the header. A field may be
/// in double quotes, inside which a comma or a line break is text and a quote is written twice. A byte order mark and
/// blank lines are skipped. Header names are compared without regard to case or the spaces around them. Every row
/// keeps the number of the line it started on, so a finding can name it.
/// </summary>
public sealed class CsvTable
{
    private readonly Dictionary<string, int> _columns;

    private CsvTable(IReadOnlyList<string> headers, IReadOnlyList<CsvRow> rows)
    {
        Headers = headers;
        Rows = rows;
        _columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < headers.Count; index++)
        {
            _columns.TryAdd(headers[index], index);
        }
    }

    /// <summary>The names of the header, trimmed, in their order.</summary>
    public IReadOnlyList<string> Headers { get; }

    /// <summary>The rows after the header, in their order.</summary>
    public IReadOnlyList<CsvRow> Rows { get; }

    /// <summary>The names among <paramref name="required"/> that the header lacks.</summary>
    public IReadOnlyList<string> Missing(params string[] required)
    {
        ArgumentNullException.ThrowIfNull(required);

        return [.. required.Where(name => !_columns.ContainsKey(name))];
    }

    /// <summary>
    /// The field of <paramref name="row"/> under <paramref name="column"/>, trimmed; null when the header has no such
    /// column, the row is shorter, or the field is blank.
    /// </summary>
    public string? Field(CsvRow row, string column)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (!_columns.TryGetValue(column, out var index) || index >= row.Fields.Count)
        {
            return null;
        }

        var value = row.Fields[index].Trim();

        return value.Length == 0 ? null : value;
    }

    /// <summary>Reads <paramref name="text"/>. A text with no line that holds anything has no header and no rows.</summary>
    public static CsvTable Parse(string? text)
    {
        var records = Records(text ?? string.Empty);
        if (records.Count == 0)
        {
            return new CsvTable([], []);
        }

        var headers = records[0].Fields.Select(name => name.Trim()).ToArray();

        return new CsvTable(headers, [.. records.Skip(1)]);
    }

    private static List<CsvRow> Records(string text)
    {
        var records = new List<CsvRow>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var wasQuoted = false;
        var line = 1;
        var startLine = 1;
        var position = text.Length > 0 && text[0] == '﻿' ? 1 : 0;

        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
            wasQuoted = false;
        }

        void EndRecord()
        {
            EndField();
            if (fields.Count > 1 || fields[0].Trim().Length > 0)
            {
                records.Add(new CsvRow(startLine, [.. fields]));
            }

            fields.Clear();
        }

        for (; position < text.Length; position++)
        {
            var character = text[position];
            if (quoted)
            {
                if (character == '"')
                {
                    if (position + 1 < text.Length && text[position + 1] == '"')
                    {
                        field.Append('"');
                        position++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    if (character == '\n')
                    {
                        line++;
                    }

                    field.Append(character);
                }

                continue;
            }

            switch (character)
            {
                case '"' when field.Length == 0 && !wasQuoted:
                    quoted = true;
                    wasQuoted = true;
                    break;
                case ',':
                    EndField();
                    break;
                case '\r':
                    break;
                case '\n':
                    EndRecord();
                    line++;
                    startLine = line;
                    break;
                default:
                    field.Append(character);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0 || wasQuoted)
        {
            EndRecord();
        }

        return records;
    }
}
