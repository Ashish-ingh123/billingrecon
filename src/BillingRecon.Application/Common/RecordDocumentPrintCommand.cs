using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Common;

/// <summary>A user prints a document (ADR 0054): the print is recorded, and the answer says whether it is the original.</summary>
public sealed record RecordDocumentPrintCommand(PrintedDocumentType DocumentType, Guid DocumentId);
