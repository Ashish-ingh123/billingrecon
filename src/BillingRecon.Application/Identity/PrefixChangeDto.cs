using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>A prefix change (docs/api.md section 8.14): which type, from which prefix to which, where it stands, and who asked when.</summary>
public sealed record PrefixChangeDto(
    Guid Id,
    DocumentType DocumentType,
    string FromPrefix,
    string ToPrefix,
    PrefixChangeStatus Status,
    DateTimeOffset RequestedAtUtc,
    Guid? RequestedByUserId,
    DateTimeOffset? DecidedAtUtc,
    string Version);
