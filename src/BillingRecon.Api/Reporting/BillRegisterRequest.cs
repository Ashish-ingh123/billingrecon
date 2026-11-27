using BillingRecon.Api.Common;
using BillingRecon.Application.Common;
using BillingRecon.Application.Reporting;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Reporting;

/// <summary>
/// The query string of <c>GET /api/v1/reports/bill-register</c>: <c>from</c> and <c>to</c> (both or neither),
/// <c>payerId</c>, <c>status</c>, <c>page</c> and <c>pageSize</c>.
/// </summary>
public sealed record BillRegisterRequest
{
    /// <summary>The first bill date, <c>yyyy-MM-dd</c>, with <see cref="To"/>; neither means the current month in India.</summary>
    public DateOnly? From { get; init; }

    /// <summary>The last bill date, <c>yyyy-MM-dd</c>, with <see cref="From"/>.</summary>
    public DateOnly? To { get; init; }

    /// <summary>Only the bills owed by this payer.</summary>
    public Guid? PayerId { get; init; }

    /// <summary>Only the bills in this state.</summary>
    public ReportBillStatus? Status { get; init; }

    /// <summary>Only the bills whose total is this amount or more.</summary>
    public string? TotalFrom { get; init; }

    /// <summary>Only the bills whose total is this amount or less.</summary>
    public string? TotalTo { get; init; }

    /// <summary>
    /// The order: one of the list's columns (<c>number</c>, <c>billDate</c>, <c>total</c>, <c>balanceDue</c>), with <c>-</c> in front for the largest or
    /// latest first. The list's own order when left out.
    /// </summary>
    public string? Sort { get; init; }

    /// <summary>The page, from 1; the first when left out.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page, 1 to 100; 20 when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query, with the defaults of <see cref="Paging"/>.</summary>
    public Result<BillRegisterQuery> ToQuery()
    {
        var read = new FilterReader();
        var totalFrom = read.Amount(TotalFrom, "totalFrom");
        var totalTo = read.Amount(TotalTo, "totalTo");
        read.InOrder(totalFrom, totalTo, "totalTo");
        var order = read.Sort(Sort, BillRegisterQuery.Sorts);

        return read.Answer(new BillRegisterQuery
        {
            From = From,
            To = To,
            PayerId = PayerId is { } payerId ? new PayerId(payerId) : null,
            Status = Status,
            Page = Page ?? Paging.FirstPage,
            PageSize = PageSize ?? Paging.DefaultPageSize,
            TotalFrom = totalFrom,
            TotalTo = totalTo,
            Sort = order,
        });
    }
}
