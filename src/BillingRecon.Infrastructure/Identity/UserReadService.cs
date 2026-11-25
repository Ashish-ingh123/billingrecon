using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>Reads users for the API, without tracking. The tenant filter of the context applies. The e-mail filter finds a part of
/// the address (Prompt 10.2b), with the wildcards of LIKE escaped, so a <c>%</c> or <c>_</c> in it matches itself.</summary>
public sealed class UserReadService(BillingReconDbContext context) : IUserReadService
{
    /// <inheritdoc />
    public async Task<UserDto?> GetAsync(UserId id, CancellationToken ct)
    {
        var user = await Users().SingleOrDefaultAsync(candidate => candidate.Id == id, ct);

        return user?.ToDto();
    }

    /// <inheritdoc />
    public async Task<PageOf<UserDto>> ListAsync(ListUsersQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var users = Users();
        if (query.Status is { } status)
        {
            users = users.Where(user => user.Status == status);
        }

        if (query.Role is { } role)
        {
            users = users.Where(user => user.Roles.Any(candidate => candidate.Role == role));
        }

        if (query.Email is { } email)
        {
            var pattern = LikePattern.Containing(User.Normalize(email));
            users = users.Where(user => EF.Functions.Like(user.NormalizedEmail, pattern, LikePattern.Escape));
        }

        var totalCount = await users.CountAsync(ct);
        var page = await users
            .OrderBy(user => user.NormalizedEmail)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PageOf<UserDto>([.. page.Select(user => user.ToDto())], query.Page, query.PageSize, totalCount);
    }

    private IQueryable<User> Users() => context.Set<User>().AsNoTracking().Include(user => user.Roles);
}
