namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// What <see cref="StatementReader"/> found in a file: why it cannot be read at all, or the rows after the header, the
/// lines read from them and the rows refused, each with its row number.
/// </summary>
public sealed record StatementReading(
    string? UnreadableReason,
    int RowsRead,
    IReadOnlyList<ReadStatementRow> Lines,
    IReadOnlyList<StatementRefusal> Refusals)
{
    /// <summary>A file that cannot be read as a whole, for <paramref name="reason"/>.</summary>
    public static StatementReading Unreadable(string reason) => new(reason, 0, [], []);
}
