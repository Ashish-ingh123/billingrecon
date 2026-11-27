using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>Reads a user's hold by the key of the row, through the context and its tenant filter, and keeps nothing (ADR 0061).</summary>
public sealed class ApprovalHolds(BillingReconDbContext context) : IApprovalHolds
{
    /// <inheritdoc />
    public Task<DateTimeOffset?> HoldUntilAsync(UserId userId, CancellationToken ct)
        => context.Set<User>()
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.ApprovalHoldUntilUtc)
            .SingleOrDefaultAsync(ct);
}
