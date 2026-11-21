using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Api.Identity;

/// <summary>The query string of <c>GET /api/v1/users</c>: <c>status</c>, <c>role</c>, <c>email</c> (the start of the address), <c>page</c> and <c>pageSize</c>, all optional.</summary>
public sealed record ListUsersRequest
{
    /// <summary>Only the users in this state.</summary>
    public UserStatus? Status { get; init; }

    /// <summary>Only the users who hold this role.</summary>
    public Role? Role { get; init; }

    /// <summary>Only the users whose address starts with this, without regard to case.</summary>
    public string? Email { get; init; }

    /// <summary>From 1; 1 when left out.</summary>
    public int? Page { get; init; }

    /// <summary>From 1 to the largest page size; the default page size when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query the request stands for.</summary>
    public ListUsersQuery ToQuery() => new()
    {
        Status = Status,
        Role = Role,
        Email = Email,
        Page = Page ?? Paging.FirstPage,
        PageSize = PageSize ?? Paging.DefaultPageSize,
    };
}
