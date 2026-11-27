using BillingRecon.Application.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>A page of the users of the tenant, by status, role and a part of the e-mail address; the filters combine.</summary>
public sealed record ListUsersQuery : IPagedQuery
{
    /// <summary>The columns this list is sorted by on request (ADR 0063); each stands for an expression of the query service.</summary>
    public static IReadOnlyList<string> Sorts { get; } = ["displayName", "email", "lastSignInAtUtc"];

    /// <summary>Only the users in this state.</summary>
    public UserStatus? Status { get; init; }

    /// <summary>Only the users who hold this role.</summary>
    public Role? Role { get; init; }

    /// <summary>Only the users whose address holds this text anywhere, without regard to case.</summary>
    public string? Email { get; init; }

    /// <summary>The order asked for, one of <see cref="Sorts"/>; null for the list's own order.</summary>
    public ListSort? Order { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
