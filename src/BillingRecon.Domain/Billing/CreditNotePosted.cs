using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// A credit note was approved and posted under its number (BR-212, BR-606): revenue falls by its total, and the
/// payer has that much credit available until it is applied.
/// </summary>
public sealed record CreditNotePosted(CreditNoteId CreditNoteId, string DocumentNumber, BillId BillId, PayerId PayerId, Money Total, DateOnly CreditNoteDate)
    : IDomainEvent, IHasDocumentNumber;
