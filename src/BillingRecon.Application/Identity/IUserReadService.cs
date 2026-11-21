using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>Reads users for the API, without tracking. What it answers never holds a password hash.</summary>
public interface IUserReadService
{
    /// <summary>The user with that id; null when the tenant has none.</summary>
    public Task<UserDto?> GetAsync(UserId id, CancellationToken ct);

    /// <summary>A page of the users that match the query, by e-mail address.</summary>
    public Task<PageOf<UserDto>> ListAsync(ListUsersQuery query, CancellationToken ct);
}
