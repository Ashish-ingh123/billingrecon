using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>A TenantAdmin asks for a new prefix of one document type (BR-802, ADR 0033), with an optional word for the checker.</summary>
public sealed record RequestPrefixChangeCommand(DocumentType? DocumentType, string? Prefix, string? Comment);
