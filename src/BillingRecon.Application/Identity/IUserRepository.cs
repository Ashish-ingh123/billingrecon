using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>The users of the tenant, for the use cases that change them.</summary>
public interface IUserRepository
{
    /// <summary>The user with that id, tracked, with its roles; null when the tenant has none.</summary>
    public Task<User?> GetAsync(UserId id, CancellationToken ct);

    /// <summary>
    /// Whether another user of the tenant than <paramref name="except"/> has the address <paramref name="normalizedEmail"/>
    /// (BR-601). The unique index is the last word; this answers the ordinary case without a failed save.
    /// </summary>
    public Task<bool> EmailTakenAsync(string normalizedEmail, UserId? except, CancellationToken ct);

    /// <summary>How many active tenant administrators the tenant has besides <paramref name="except"/>.</summary>
    public Task<int> CountOtherActiveTenantAdminsAsync(UserId except, CancellationToken ct);

    /// <summary>Adds a new user to the unit of work, which gives it its id.</summary>
    public void Add(User user);
}
