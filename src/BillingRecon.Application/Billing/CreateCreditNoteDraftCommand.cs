using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Creates a draft credit note against a posted bill (US-24, BR-210). A date left out is today in India. The lines may
/// be empty and can be replaced later.
/// </summary>
public sealed record CreateCreditNoteDraftCommand
{
    /// <summary>The posted bill the credit note is against.</summary>
    public required BillId BillId { get; init; }

    /// <summary>The business date of the credit note; today in India when null.</summary>
    public DateOnly? CreditNoteDate { get; init; }

    /// <summary>Why the credit is given.</summary>
    public required string Reason { get; init; }

    /// <summary>The lines, in the order they appear on the credit note.</summary>
    public IReadOnlyList<CreditNoteLineInput> Lines { get; init; } = [];
}
