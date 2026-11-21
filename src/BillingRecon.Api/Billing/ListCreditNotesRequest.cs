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

    /// <summary>The page, from 1; the first when left out.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page, 1 to 100; 20 when left out.</summary>
    public int? PageSize { get; init; }

    /// <summary>The query, with the defaults of <see cref="Paging"/>.</summary>
    public ListCreditNotesQuery ToQuery() => new()
    {
        BillId = BillId is { } billId ? new BillId(billId) : null,
        PayerId = PayerId is { } payerId ? new PayerId(payerId) : null,
        Status = Status,
        Page = Page ?? Paging.FirstPage,
        PageSize = PageSize ?? Paging.DefaultPageSize,
    };
}
