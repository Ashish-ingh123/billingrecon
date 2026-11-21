using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>A checker approved a prefix change; the document type takes its new prefix (BR-802, ADR 0033).</summary>
public sealed record PrefixChangeApproved(PrefixChangeId PrefixChangeId, DocumentType DocumentType, string FromPrefix, string ToPrefix) : IDomainEvent;
