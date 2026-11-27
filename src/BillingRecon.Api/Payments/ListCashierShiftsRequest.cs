using BillingRecon.Application.Common;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Api.Payments;

/// <summary>The query string of <c>GET /api/v1/cashier-shifts</c>.</summary>
public sealed record ListCashierShiftsRequest
{
    /// <summary>Only the shifts of this cashier.</summary>
    public Guid? CashierUserId { get; init; }

    /// <summary>Only the shifts of the user who asks.</summary>
    public bool? Mine { get; init; }

    /// <summary>Only the shifts in this state: Open, PendingApproval or Closed.</summary>
    public CashierShiftStatus? Status { get; init; }

    /// <summary>Only the shifts that are open or wait for approval.</summary>
    public bool? Unclosed { get; init; }

    /// <summary>Only the shifts of this business date.</summary>
    public DateOnly? Date { get; init; }

    /// <summary>The page, from 1.</summary>
    public int? Page { get; init; }

    /// <summary>The size of a page, at most 100.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query of the use case.</summary>
    public ListCashierShiftsQuery ToQuery() => new()
    {
        CashierUserId = CashierUserId is { } cashier ? new UserId(cashier) : null,
        Mine = Mine ?? false,
        Status = Status,
        Unclosed = Unclosed ?? false,
        Date = Date,
        Page = Page ?? Paging.FirstPage,
        PageSize = PageSize ?? Paging.DefaultPageSize,
    };
}
