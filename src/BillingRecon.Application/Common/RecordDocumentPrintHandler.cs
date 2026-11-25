using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Common;

/// <summary>
/// Records that a document is being printed (ADR 0054) and answers which print it is: the first is the original, every
/// later one a duplicate. The document must exist and be one that is printed. Two prints of the same document at the
/// same instant cannot both be the original: the unique index of the copy number refuses the second, which answers a
/// conflict and is sent again.
/// </summary>
public sealed class RecordDocumentPrintHandler(
    IClock clock,
    ICurrentUser currentUser,
    IPrintableDocuments documents,
    IDocumentPrintRepository prints,
    IUnitOfWork unitOfWork) : ICommandHandler<RecordDocumentPrintCommand, DocumentPrintDto>
{
    /// <inheritdoc />
    public async Task<Result<DocumentPrintDto>> HandleAsync(RecordDocumentPrintCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var printable = await documents.EnsurePrintableAsync(command.DocumentType, command.DocumentId, ct);
        if (printable.IsFailure)
        {
            return printable.Error;
        }

        var copyNumber = await prints.CountAsync(command.DocumentType, command.DocumentId, ct) + 1;
        var print = DocumentPrint.Record(command.DocumentType, command.DocumentId, copyNumber, clock.UtcNow, currentUser.UserId);
        prints.Add(print);

        var committed = await unitOfWork.CommitAsync(ct);
        if (committed.IsFailure)
        {
            return committed.Error.Code == CommonErrors.DuplicateKeyCode ? CommonErrors.ConcurrencyConflict() : committed.Error;
        }

        return new DocumentPrintDto(print.CopyNumber, print.IsOriginal, print.PrintedAtUtc);
    }
}
