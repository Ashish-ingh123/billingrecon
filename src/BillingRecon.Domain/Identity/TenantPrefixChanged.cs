using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>The tenant's default prefix of a document type changed, by an approved prefix change (BR-802, ADR 0033).</summary>
public sealed record TenantPrefixChanged(DocumentType DocumentType, string FromPrefix, string ToPrefix) : IDomainEvent;
