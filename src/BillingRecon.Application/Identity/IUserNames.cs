using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>
/// What Identity answers when another module has to name a user of the hospital (ADR 0059): the name a receipt or a
/// refund voucher prints for who recorded it. The name and nothing else: no address, no role.
/// </summary>
public interface IUserNames
{
    /// <summary>The display name of the user of the current tenant; null when there is no such user.</summary>
    public Task<string?> FindDisplayNameAsync(UserId userId, CancellationToken ct);
}
