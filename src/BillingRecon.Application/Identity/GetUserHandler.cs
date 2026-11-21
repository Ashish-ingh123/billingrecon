using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>Reads one user of the tenant; another tenant's is not found.</summary>
public sealed class GetUserHandler(IUserReadService users) : IQueryHandler<GetUserQuery, UserDto>
{
    /// <inheritdoc />
    public async Task<Result<UserDto>> HandleAsync(GetUserQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await users.GetAsync(query.Id, ct);

        return user is null ? IdentityErrors.UserNotFound() : user;
    }
}
