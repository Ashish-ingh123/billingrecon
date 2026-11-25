using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// Reads the user's row by its key on every request with a token (ADR 0057): nothing is kept, so a sign-out, a disabling
/// or a change of the roles holds from the next request on. The filter of the context shows a tenant its own users alone,
/// so a token whose user is of another tenant is not current.
/// </summary>
public sealed class UserSessionReader(BillingReconDbContext context) : IUserSessionReader
{
    /// <inheritdoc />
    public async Task<bool> IsCurrentAsync(UserId userId, int sessionVersion, CancellationToken ct)
    {
        var current = await context.Set<User>()
            .AsNoTracking()
            .Where(user => user.Id == userId && user.Status == UserStatus.Active)
            .Select(user => (int?)user.SessionVersion)
            .SingleOrDefaultAsync(ct);

        return current == sessionVersion;
    }
}
