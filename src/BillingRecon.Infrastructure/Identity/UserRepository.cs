using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>The users of the tenant, tracked, with their roles. The tenant filter of the context applies.</summary>
public sealed class UserRepository(BillingReconDbContext context) : IUserRepository
{
    /// <inheritdoc />
    public Task<User?> GetAsync(UserId id, CancellationToken ct)
        => context.Set<User>().Include(user => user.Roles).SingleOrDefaultAsync(user => user.Id == id, ct);

    /// <inheritdoc />
    public Task<bool> EmailTakenAsync(string normalizedEmail, UserId? except, CancellationToken ct)
    {
        var holders = context.Set<User>().Where(user => user.NormalizedEmail == normalizedEmail);
        if (except is { } self)
        {
            holders = holders.Where(user => user.Id != self);
        }

        return holders.AnyAsync(ct);
    }

    /// <inheritdoc />
    public Task<int> CountOtherActiveTenantAdminsAsync(UserId except, CancellationToken ct)
        => context.Set<User>().CountAsync(
            user => user.Id != except && user.Status == UserStatus.Active && user.Roles.Any(role => role.Role == Role.TenantAdmin),
            ct);

    /// <inheritdoc />
    public void Add(User user) => context.Set<User>().Add(user);
}
