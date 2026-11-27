using BillingRecon.Api.Common;
using BillingRecon.Application.Common;
using BillingRecon.Application.Reporting;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Reporting;

/// <summary>
/// The query string of <c>GET /api/v1/reports/unapplied-cash</c> and <c>GET /api/v1/reports/available-credit</c>:
/// <c>payerId</c>, <c>page</c> and <c>pageSize</c>, all optional.
/// </summary>
public sealed record PayerDocumentsRequest
{
    /// <summary>Only the documents of this payer.</summary>
    public Guid? PayerId { get; init; }

    /// <summary>Only the documents dated on or after this day (<c>yyyy-MM-dd</c>).</summary>
    public string? DateFrom { get; init; }

    /// <summary>Only the documents dated on or before this day (<c>yyyy-MM-dd</c>).</summary>
    public string? DateTo { get; init; }

    /// <summary>
    /// The order: one of the report's columns (<c>number</c>, then <c>paymentDate</c> and <c>unappliedAmount</c> for
    /// the unapplied cash, <c>creditNoteDate</c> and <c>availableAmount</c> for the available credit), with <c>-</c> in
    /// front for the largest or latest first. The report's own order when left out.
    /// </summary>
    public string? Sort { get; init; }

    /// <summary>The page, from 1; the first when left out.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page, 1 to 100; 20 when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query of the unapplied cash, with the defaults of <see cref="Paging"/>.</summary>
    public Result<UnappliedCashQuery> ToUnappliedCashQuery()
    {
        var read = new FilterReader();
        var dateFrom = read.Date(DateFrom, "dateFrom");
        var dateTo = read.Date(DateTo, "dateTo");
        read.InOrder(dateFrom, dateTo, "dateTo");
        var order = read.Sort(Sort, UnappliedCashQuery.Sorts);

        return read.Answer(new UnappliedCashQuery
        {
            PayerId = PayerId is { } payerId ? new PayerId(payerId) : null,
            Page = Page ?? Paging.FirstPage,
            PageSize = PageSize ?? Paging.DefaultPageSize,
            DateFrom = dateFrom,
            DateTo = dateTo,
            Sort = order,
        });
    }

    /// <summary>The query of the available credit, with the defaults of <see cref="Paging"/>.</summary>
    public Result<AvailableCreditQuery> ToAvailableCreditQuery()
    {
        var read = new FilterReader();
        var dateFrom = read.Date(DateFrom, "dateFrom");
        var dateTo = read.Date(DateTo, "dateTo");
        read.InOrder(dateFrom, dateTo, "dateTo");
        var order = read.Sort(Sort, AvailableCreditQuery.Sorts);

        return read.Answer(new AvailableCreditQuery
        {
            PayerId = PayerId is { } payerId ? new PayerId(payerId) : null,
            Page = Page ?? Paging.FirstPage,
            PageSize = PageSize ?? Paging.DefaultPageSize,
            DateFrom = dateFrom,
            DateTo = dateTo,
            Sort = order,
        });
    }
}
