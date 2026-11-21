using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>A checker rejects a prefix change (BR-603, BR-605), at the version read, saying why.</summary>
public sealed record RejectPrefixChangeCommand(PrefixChangeId Id, string ExpectedVersion, string? Comment);
