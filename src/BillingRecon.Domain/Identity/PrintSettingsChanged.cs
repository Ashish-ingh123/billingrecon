using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>The print settings of one kind of document were set or changed (ADR 0059). The audit images hold what they were and are.</summary>
public sealed record PrintSettingsChanged(PrintedDocumentType DocumentType) : IDomainEvent;
