using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>The names of the tenant's users, through the context and its tenant filter (ADR 0059).</summary>
public sealed class UserNames(BillingReconDbContext context) : IUserNames
{
    /// <inheritdoc />
    public Task<string?> FindDisplayNameAsync(UserId userId, CancellationToken ct)
        => context.Set<User>().Where(user => user.Id == userId).Select(user => (string?)user.DisplayName).SingleOrDefaultAsync(ct);
}
