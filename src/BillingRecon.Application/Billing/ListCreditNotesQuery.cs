using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Lists the tenant's credit notes one page at a time, the newest date first. Every filter is optional and they
/// combine: the bill, the payer and the status.
/// </summary>
public sealed record ListCreditNotesQuery : IPagedQuery
{
    /// <summary>Only the credit notes against this bill.</summary>
    public BillId? BillId { get; init; }

    /// <summary>Only the credit notes of this payer.</summary>
    public PayerId? PayerId { get; init; }

    /// <summary>Only the credit notes in this status.</summary>
    public CreditNoteStatus? Status { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
