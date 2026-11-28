namespace BillingRecon.Application.Reconciliation;

/// <summary>An import of a bank's statement as the API returns it (BR-1003, BR-1004): what was read, added, held before and refused.</summary>
public sealed record StatementImportDto(
    Guid Id,
    Guid ChannelId,
    string FileName,
    string FileHash,
    int RowsRead,
    int LinesAdded,
    int LinesAlreadyHeld,
    int RowsRefused,
    DateOnly? FirstDate,
    DateOnly? LastDate,
    DateTimeOffset CreatedAtUtc,
    Guid? CreatedByUserId);
