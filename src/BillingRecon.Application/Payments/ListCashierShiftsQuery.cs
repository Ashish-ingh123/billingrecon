using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>Lists shifts, the newest first. A user who reads only their own shifts gets their own, whatever is asked.</summary>
public sealed record ListCashierShiftsQuery : IPagedQuery
{
    /// <summary>Only the shifts of this cashier.</summary>
    public UserId? CashierUserId { get; init; }

    /// <summary>Only the shifts of the user who asks.</summary>
    public bool Mine { get; init; }

    /// <summary>Only the shifts in this state.</summary>
    public CashierShiftStatus? Status { get; init; }

    /// <summary>Only the shifts that are not closed: open, or waiting for approval.</summary>
    public bool Unclosed { get; init; }

    /// <summary>Only the shifts of this business date.</summary>
    public DateOnly? Date { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
