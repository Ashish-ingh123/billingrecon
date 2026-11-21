using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>A checker approves a prefix change (BR-603, BR-802), at the version read, with an optional comment.</summary>
public sealed record ApprovePrefixChangeCommand(PrefixChangeId Id, string ExpectedVersion, string? Comment);
