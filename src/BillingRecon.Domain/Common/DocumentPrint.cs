namespace BillingRecon.Domain.Common;

/// <summary>
/// The record that a document was printed (ADR 0054): which one, by whom, when, and the how-manieth print of it this
/// is. The first print of a document is its original and every later one a duplicate, and the printed page says which.
/// A row is written once and never changed or deleted: it is itself the trail of the prints, so it raises no event and
/// has no audit record beside it.
/// </summary>
public sealed class DocumentPrint : Entity<DocumentPrintId>, ITenantScoped, IAppendOnly
{
    // Entity Framework creates a record with this constructor and then sets the properties.
    private DocumentPrint()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The kind of document that was printed.</summary>
    public PrintedDocumentType DocumentType { get; private set; }

    /// <summary>The id of the bill, payment or refund.</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>1 for the original, 2 and up for the duplicates; gapless per document.</summary>
    public int CopyNumber { get; private set; }

    /// <summary>When it was printed.</summary>
    public DateTimeOffset PrintedAtUtc { get; private set; }

    /// <summary>Who printed it; null when no user did.</summary>
    public UserId? PrintedByUserId { get; private set; }

    /// <summary>Whether this print is the original of its document.</summary>
    public bool IsOriginal => CopyNumber == 1;

    /// <summary>The record of print number <paramref name="copyNumber"/> of a document.</summary>
    /// <exception cref="ArgumentException">The kind is not defined, the id is empty, or the number is below 1.</exception>
    public static DocumentPrint Record(PrintedDocumentType documentType, Guid documentId, int copyNumber, DateTimeOffset printedAtUtc, UserId? printedBy)
    {
        if (!Enum.IsDefined(documentType))
        {
            throw new ArgumentException("The kind of document is not one the enum defines.", nameof(documentType));
        }

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("A print is of a document; the empty id is not one.", nameof(documentId));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(copyNumber, 1);

        return new DocumentPrint
        {
            DocumentType = documentType,
            DocumentId = documentId,
            CopyNumber = copyNumber,
            PrintedAtUtc = printedAtUtc,
            PrintedByUserId = printedBy,
        };
    }
}
