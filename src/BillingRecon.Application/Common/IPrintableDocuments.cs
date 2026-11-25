using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Common;

/// <summary>
/// Whether a document can be printed (ADR 0054). Only what took financial effect is printed for a patient: a posted
/// bill, a payment that was not reversed, a posted refund. A draft or a document that waits for approval is not one yet.
/// </summary>
public interface IPrintableDocuments
{
    /// <summary>
    /// Success when the tenant has the document and it can be printed; otherwise the error of its module when it does
    /// not exist, or <c>document_not_printable</c> when it exists and is not in a state that is printed.
    /// </summary>
    public Task<Result> EnsurePrintableAsync(PrintedDocumentType documentType, Guid documentId, CancellationToken ct);
}
