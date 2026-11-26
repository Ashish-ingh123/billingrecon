using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>
/// An image of the tenant's letterhead was set, replaced or removed (ADR 0059). The event and the audit images carry
/// the SHA-256 of the image before and after, never the image; a null hash is no image.
/// </summary>
public sealed record TenantLetterheadImageChanged(LetterheadImageKind Kind, string? PreviousSha256, string? Sha256) : IDomainEvent;
