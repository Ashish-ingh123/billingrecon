using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>A TenantAdmin asked for a new prefix of a document type (BR-802, ADR 0033).</summary>
public sealed record PrefixChangeRequested(DocumentType DocumentType, string FromPrefix, string ToPrefix) : IDomainEvent;
