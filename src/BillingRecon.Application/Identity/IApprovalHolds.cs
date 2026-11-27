using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>
/// Until when a user decides nothing (ADR 0061; review finding R5): read, uncached, before a decision is let through,
/// so a hold an administrator's reset set a moment ago holds on every instance.
/// </summary>
public interface IApprovalHolds
{
    /// <summary>The end of the user's hold; null when the user has none, or is not found.</summary>
    public Task<DateTimeOffset?> HoldUntilAsync(UserId userId, CancellationToken ct);
}
