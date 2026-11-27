using BillingRecon.Api.Common;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Billing;

/// <summary>
/// The query string of <c>GET /api/v1/bills</c>: <c>patientId</c>, <c>payerId</c>, <c>status</c>, <c>billDateFrom</c>,
/// <c>billDateTo</c>, <c>page</c> and <c>pageSize</c>, all optional.
/// </summary>
public sealed record ListBillsRequest
{
    /// <summary>Only the bills for this patient.</summary>
    public Guid? PatientId { get; init; }

    /// <summary>Only the bills owed by this payer.</summary>
    public Guid? PayerId { get; init; }

    /// <summary>Draft or Voided; both when left out.</summary>
    public BillStatus? Status { get; init; }

    /// <summary>Only the bills dated on or after this day (<c>yyyy-MM-dd</c>).</summary>
    public DateOnly? BillDateFrom { get; init; }

    /// <summary>Only the bills dated on or before this day (<c>yyyy-MM-dd</c>).</summary>
    public DateOnly? BillDateTo { get; init; }

    /// <summary>
    /// Only the documents the text finds (Prompt 11.1): a part of the number, of the patient's name or MRN, of the
    /// payer's name, or exactly the amount it states. Fewer than two characters filter nothing.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>Only the bills whose total is this amount or more.</summary>
    public string? TotalFrom { get; init; }

    /// <summary>Only the bills whose total is this amount or less.</summary>
    public string? TotalTo { get; init; }

    /// <summary>Only the bills that still owe this amount or more.</summary>
    public string? BalanceDueFrom { get; init; }

    /// <summary>Only the bills that still owe this amount or less.</summary>
    public string? BalanceDueTo { get; init; }

    /// <summary>Only the bills this user made (the id of a user).</summary>
    public string? CreatedBy { get; init; }

    /// <summary>Only the bills of this origin: Standard, or Opening for those brought in at the cut-over.</summary>
    public string? Origin { get; init; }

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
    public Result<ListBillsQuery> ToQuery()
    {
        var read = new FilterReader();
        var totalFrom = read.Amount(TotalFrom, "totalFrom");
        var totalTo = read.Amount(TotalTo, "totalTo");
        var balanceDueFrom = read.Amount(BalanceDueFrom, "balanceDueFrom");
        var balanceDueTo = read.Amount(BalanceDueTo, "balanceDueTo");
        var createdBy = read.Id(CreatedBy, "createdBy");
        var origin = read.OneOf<DocumentOrigin>(Origin, "origin");
        read.InOrder(totalFrom, totalTo, "totalTo");
        read.InOrder(balanceDueFrom, balanceDueTo, "balanceDueTo");
        var order = read.Sort(Sort, ListBillsQuery.Sorts);

        return read.Answer(new ListBillsQuery
        {
            PatientId = PatientId is { } patientId ? new PatientId(patientId) : null,
            PayerId = PayerId is { } payerId ? new PayerId(payerId) : null,
            Status = Status,
            BillDateFrom = BillDateFrom,
            BillDateTo = BillDateTo,
            Search = Search,
            Page = Page ?? Paging.FirstPage,
            PageSize = PageSize ?? Paging.DefaultPageSize,
            TotalFrom = totalFrom,
            TotalTo = totalTo,
            BalanceDueFrom = balanceDueFrom,
            BalanceDueTo = balanceDueTo,
            CreatedBy = createdBy is { } createdById ? new UserId(createdById) : null,
            Origin = origin,
            Order = order,
        });
    }
}
