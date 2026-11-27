using BillingRecon.Api.Common;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Billing;

/// <summary>The query string of <c>GET /api/v1/credit-notes</c>: <c>billId</c>, <c>payerId</c>, <c>status</c>, <c>page</c> and <c>pageSize</c>, all optional.</summary>
public sealed record ListCreditNotesRequest
{
    /// <summary>Only the credit notes against this bill.</summary>
    public Guid? BillId { get; init; }

    /// <summary>Only the credit notes of this payer.</summary>
    public Guid? PayerId { get; init; }

    /// <summary>Draft, PendingApproval, Posted or Voided; all when left out.</summary>
    public CreditNoteStatus? Status { get; init; }

    /// <summary>
    /// Only the documents the text finds (Prompt 11.1): a part of the number, of the patient's name or MRN, of the
    /// payer's name, or exactly the amount it states. Fewer than two characters filter nothing.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>Only the credit notes dated on or after this day (<c>yyyy-MM-dd</c>).</summary>
    public string? CreditNoteDateFrom { get; init; }

    /// <summary>Only the credit notes dated on or before this day (<c>yyyy-MM-dd</c>).</summary>
    public string? CreditNoteDateTo { get; init; }

    /// <summary>Only the credit notes whose total is this amount or more.</summary>
    public string? TotalFrom { get; init; }

    /// <summary>Only the credit notes whose total is this amount or less.</summary>
    public string? TotalTo { get; init; }

    /// <summary>Only the credit notes this user made (the id of a user).</summary>
    public string? CreatedBy { get; init; }

    /// <summary>
    /// The order: one of the list's columns (<c>number</c>, <c>creditNoteDate</c>, <c>total</c>, <c>availableAmount</c>), with <c>-</c> in front for the largest or
    /// latest first. The list's own order when left out.
    /// </summary>
    public string? Sort { get; init; }

    /// <summary>The page, from 1; the first when left out.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page, 1 to 100; 20 when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query, with the defaults of <see cref="Paging"/>.</summary>
    public Result<ListCreditNotesQuery> ToQuery()
    {
        var read = new FilterReader();
        var creditNoteDateFrom = read.Date(CreditNoteDateFrom, "creditNoteDateFrom");
        var creditNoteDateTo = read.Date(CreditNoteDateTo, "creditNoteDateTo");
        var totalFrom = read.Amount(TotalFrom, "totalFrom");
        var totalTo = read.Amount(TotalTo, "totalTo");
        var createdBy = read.Id(CreatedBy, "createdBy");
        read.InOrder(creditNoteDateFrom, creditNoteDateTo, "creditNoteDateTo");
        read.InOrder(totalFrom, totalTo, "totalTo");
        var order = read.Sort(Sort, ListCreditNotesQuery.Sorts);

        return read.Answer(new ListCreditNotesQuery
        {
            BillId = BillId is { } billId ? new BillId(billId) : null,
            PayerId = PayerId is { } payerId ? new PayerId(payerId) : null,
            Status = Status,
            Search = Search,
            Page = Page ?? Paging.FirstPage,
            PageSize = PageSize ?? Paging.DefaultPageSize,
            CreditNoteDateFrom = creditNoteDateFrom,
            CreditNoteDateTo = creditNoteDateTo,
            TotalFrom = totalFrom,
            TotalTo = totalTo,
            CreatedBy = createdBy is { } createdById ? new UserId(createdById) : null,
            Order = order,
        });
    }
}
