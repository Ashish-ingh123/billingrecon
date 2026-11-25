using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>The recorded prints of the tenant's documents, through the context and its tenant filter (ADR 0054).</summary>
public sealed class DocumentPrintRepository(BillingReconDbContext context) : IDocumentPrintRepository
{
    /// <inheritdoc />
    public Task<int> CountAsync(PrintedDocumentType documentType, Guid documentId, CancellationToken ct)
        => context.Set<DocumentPrint>().CountAsync(print => print.DocumentType == documentType && print.DocumentId == documentId, ct);

    /// <inheritdoc />
    public void Add(DocumentPrint print) => context.Set<DocumentPrint>().Add(print);
}
