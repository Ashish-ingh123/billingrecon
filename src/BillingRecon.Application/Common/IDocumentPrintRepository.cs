using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Common;

/// <summary>The recorded prints of the tenant's documents (ADR 0054): counted, and added to; never changed.</summary>
public interface IDocumentPrintRepository
{
    /// <summary>How many prints of the document are recorded.</summary>
    public Task<int> CountAsync(PrintedDocumentType documentType, Guid documentId, CancellationToken ct);

    /// <summary>Adds a new record to the unit of work.</summary>
    public void Add(DocumentPrint print);
}
