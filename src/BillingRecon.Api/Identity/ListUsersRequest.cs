using BillingRecon.Api.Common;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Api.Identity;

/// <summary>The query string of <c>GET /api/v1/users</c>: <c>status</c>, <c>role</c>, <c>email</c> (a part of the address), <c>page</c> and <c>pageSize</c>, all optional.</summary>
public sealed record ListUsersRequest
{
    /// <summary>Only the users in this state.</summary>
    public UserStatus? Status { get; init; }

    /// <summary>Only the users who hold this role.</summary>
    public Role? Role { get; init; }

    /// <summary>Only the users whose address holds this text anywhere, without regard to case.</summary>
    public string? Email { get; init; }

    /// <summary>
    /// The order: one of the list's columns (<c>displayName</c>, <c>email</c>, <c>lastSignInAtUtc</c>), with <c>-</c> in front for the largest or
    /// latest first. The list's own order when left out.
    /// </summary>
    public string? Sort { get; init; }

    /// <summary>From 1; 1 when left out.</summary>
    public int? Page { get; init; }

    /// <summary>From 1 to the largest page size; the default page size when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query the request stands for.</summary>
    public Result<ListUsersQuery> ToQuery()
    {
        var read = new FilterReader();
        var order = read.Sort(Sort, ListUsersQuery.Sorts);

        return read.Answer(new ListUsersQuery
        {
            Status = Status,
            Role = Role,
            Email = Email,
            Page = Page ?? Paging.FirstPage,
            PageSize = PageSize ?? Paging.DefaultPageSize,
            Order = order,
        });
    }
}
