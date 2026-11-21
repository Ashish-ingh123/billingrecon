using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>Lists the tenant's credit notes one page at a time, the newest date first.</summary>
public sealed class ListCreditNotesHandler(IValidator<ListCreditNotesQuery> validator, ICreditNoteReadService creditNotes)
    : IQueryHandler<ListCreditNotesQuery, PageOf<CreditNoteSummaryDto>>
{
    /// <inheritdoc />
    public async Task<Result<PageOf<CreditNoteSummaryDto>>> HandleAsync(ListCreditNotesQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        return await creditNotes.ListAsync(query, ct);
    }
}
