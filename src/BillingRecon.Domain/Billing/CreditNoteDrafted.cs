using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// A draft credit note was created against a posted bill (US-24). It carries no credit note id: the id is assigned
/// when the credit note is added to the unit of work, after the event was raised.
/// </summary>
public sealed record CreditNoteDrafted(BillId BillId, PayerId PayerId, DateOnly CreditNoteDate, Money Total) : IDomainEvent;
