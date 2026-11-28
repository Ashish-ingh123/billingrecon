namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// What importing a file would do (BR-1003), with nothing stored: the file's hash, which the import sends back so that it
/// imports exactly this file; the counts; the money of the lines it would add; every refused row with its reason; and the
/// first <see cref="DryRunStatementHandler.PreviewLimit"/> lines it read.
/// </summary>
public sealed record StatementDryRunDto(
    string FileHash,
    bool AlreadyImported,
    int RowsRead,
    int LinesToAdd,
    int LinesAlreadyHeld,
    int RowsRefused,
    decimal MoneyIn,
    decimal MoneyOut,
    DateOnly? FirstDate,
    DateOnly? LastDate,
    IReadOnlyList<StatementRefusal> Refusals,
    IReadOnlyList<StatementPreviewLineDto> Lines);
