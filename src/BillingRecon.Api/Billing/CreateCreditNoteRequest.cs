using System.Text.Json.Serialization;
using BillingRecon.Application.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Billing;

/// <summary>The body of <c>POST /api/v1/credit-notes</c>: a draft credit note against a posted bill. Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateCreditNoteRequest
{
    /// <summary>The posted bill the credit note is against. Cannot be changed later.</summary>
    public Guid? BillId { get; init; }

    /// <summary>The business date of the credit note; today in India when left out. Not before the bill date (BR-214).</summary>
    public DateOnly? CreditNoteDate { get; init; }

    /// <summary>Why the credit is given. Required.</summary>
    public string? Reason { get; init; }

    /// <summary>The lines, in the order they appear on the credit note; may be left out and added later.</summary>
    public IReadOnlyList<CreditNoteLineRequest?>? Lines { get; init; }

    /// <summary>The command; a missing id becomes the empty id and a missing reason an empty one, which the validator reports.</summary>
    public CreateCreditNoteDraftCommand ToCommand() => new()
    {
        BillId = new BillId(BillId ?? Guid.Empty),
        CreditNoteDate = CreditNoteDate,
        Reason = Reason ?? string.Empty,
        Lines = Lines.ToInputs(),
    };
}
