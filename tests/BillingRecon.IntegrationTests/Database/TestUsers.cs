using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The user row behind an identity a database test acts as. Since Prompt 5.1 every column that names a user is a
/// foreign key to <c>Users</c> (ADR 0030), so a test identity that writes anything must be a user of its tenant. The row
/// is written as the identity itself, the only way the tenant guard lets a tenant-scoped row in, and without its domain
/// events, so that no audit record of it appears in a test. Its password matches nothing.
/// </summary>
internal static class TestUsers
{
    /// <summary>When the rows are created, as far as their stamps say.</summary>
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 17, 6, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Adds the user of <paramref name="identity"/> through <paramref name="context"/>, which acts as that identity, unless
    /// it exists. An identity without a user, or of a tenant without a row, gets none: it can write nothing anyway.
    /// </summary>
    public static async Task EnsureAsync(BillingReconDbContext context, FixedIdentity identity, IEnumerable<string> roles)
    {
        await using (context)
        {
            if (identity.UserId is not { } userId
                || !await context.Set<Tenant>().AnyAsync()
                || await context.Set<User>().AnyAsync(user => user.Id == userId))
            {
                return;
            }

            Add(context, userId, roles);
            await context.SaveChangesAsync();
        }
    }

    /// <summary>The same, synchronously, for a client made while a test sets itself up.</summary>
    public static void Ensure(BillingReconDbContext context, FixedIdentity identity, IEnumerable<string> roles)
    {
        using (context)
        {
            // The tenant filter lets a tenant see its own row only: any row seen is the identity's tenant.
            if (identity.UserId is not { } userId || !context.Set<Tenant>().Any() || context.Set<User>().Any(user => user.Id == userId))
            {
                return;
            }

            Add(context, userId, roles);
            context.SaveChanges();
        }
    }

    // The roles named by the client, as its token carries them (TestRoles).
    private static void Add(BillingReconDbContext context, UserId userId, IEnumerable<string> roleNames)
    {
        Role[] roles = [.. TestRoles.Of(roleNames)];
        var user = User.Create($"{userId.Value:N}@users.test", "Test user", Pbkdf2PasswordHasher.Unusable, roles, CreatedAt, grantedBy: null);
        context.Entry(user).Property(candidate => candidate.Id).CurrentValue = userId;
        context.Add(user);
    }
}
