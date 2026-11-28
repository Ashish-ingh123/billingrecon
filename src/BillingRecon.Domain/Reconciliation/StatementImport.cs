using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Reconciliation;

/// <summary>
/// One import of a bank's statement file into a channel (BR-1003, BR-1004; ADR 0065): the file's name and SHA-256, what
/// was read, added, already held and refused, and the dates it covers. The file itself is not kept. Insert-only: an
/// import is a fact and never changes (the context refuses an update, and the database login has no UPDATE on it).
/// </summary>
public sealed class StatementImport : AggregateRoot<StatementImportId>, ITenantScoped, ICreationAudited, IAppendOnly
{
    /// <summary>Longest allowed <see cref="FileName"/>.</summary>
    public const int FileNameMaxLength = 260;

    /// <summary>The length of <see cref="FileHash"/>: a SHA-256 in hexadecimal.</summary>
    public const int FileHashLength = 64;

    // Also the constructor Entity Framework uses to load an import; it then sets the other properties.
    private StatementImport(SettlementChannelId channelId, string fileName, string fileHash)
    {
        ChannelId = channelId;
        FileName = fileName;
        FileHash = fileHash;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The bank channel the file was imported into.</summary>
    public SettlementChannelId ChannelId { get; private set; }

    /// <summary>The name of the file as it was sent.</summary>
    public string FileName { get; private set; }

    /// <summary>The SHA-256 of the file's text, in upper-case hexadecimal: the same file is imported once per channel.</summary>
    public string FileHash { get; private set; }

    /// <summary>The rows of the file after its header.</summary>
    public int RowsRead { get; private set; }

    /// <summary>The lines this import stored.</summary>
    public int LinesAdded { get; private set; }

    /// <summary>The lines the channel already held from an earlier file (BR-1004), and that were therefore not stored again.</summary>
    public int LinesAlreadyHeld { get; private set; }

    /// <summary>The rows that were refused, each with its reason in the dry run.</summary>
    public int RowsRefused { get; private set; }

    /// <summary>The earliest transaction date of the lines read; null when no line was read.</summary>
    public DateOnly? FirstDate { get; private set; }

    /// <summary>The latest transaction date of the lines read; null when no line was read.</summary>
    public DateOnly? LastDate { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? CreatedByUserId { get; private set; }

    /// <summary>Records an import with its counts. The lines are stored beside it, each naming its id.</summary>
    /// <exception cref="ArgumentException">A value breaks a limit the use case checks.</exception>
    public static StatementImport Record(
        SettlementChannelId channelId,
        string fileName,
        string fileHash,
        int rowsRead,
        int linesAdded,
        int linesAlreadyHeld,
        int rowsRefused,
        DateOnly? firstDate,
        DateOnly? lastDate)
    {
        if (fileHash is not { Length: FileHashLength } || !fileHash.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("A file hash is a SHA-256 in hexadecimal.", nameof(fileHash));
        }

        if (rowsRead < 0 || linesAdded < 0 || linesAlreadyHeld < 0 || rowsRefused < 0 || linesAdded + linesAlreadyHeld + rowsRefused != rowsRead)
        {
            throw new ArgumentException("Every row read is added, already held or refused, once.", nameof(rowsRead));
        }

        var import = new StatementImport(channelId, Field.Required(fileName, FileNameMaxLength), fileHash.ToUpperInvariant())
        {
            RowsRead = rowsRead,
            LinesAdded = linesAdded,
            LinesAlreadyHeld = linesAlreadyHeld,
            RowsRefused = rowsRefused,
            FirstDate = firstDate,
            LastDate = lastDate,
        };
        import.AddDomainEvent(new StatementImportRecorded(channelId, import.FileName, linesAdded, linesAlreadyHeld, rowsRefused));

        return import;
    }
}
