using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>Lists the users of the tenant, a page at a time.</summary>
public sealed class ListUsersHandler(IValidator<ListUsersQuery> validator, IUserReadService users)
    : IQueryHandler<ListUsersQuery, PageOf<UserDto>>
{
    /// <inheritdoc />
    public async Task<Result<PageOf<UserDto>>> HandleAsync(ListUsersQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        return await users.ListAsync(query, ct);
    }
}
