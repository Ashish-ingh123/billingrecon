using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>Reads one credit note with its lines and applications. Another tenant's credit note is not found (BR-002).</summary>
public sealed class GetCreditNoteHandler(ICreditNoteReadService creditNotes) : IQueryHandler<GetCreditNoteQuery, CreditNoteDto>
{
    /// <inheritdoc />
    public async Task<Result<CreditNoteDto>> HandleAsync(GetCreditNoteQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var creditNote = await creditNotes.GetAsync(query.Id, ct);

        return creditNote is null ? BillingErrors.CreditNoteNotFound() : creditNote;
    }
}
