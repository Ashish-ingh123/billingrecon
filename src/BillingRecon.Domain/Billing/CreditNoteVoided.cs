using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>A draft credit note was voided: it keeps its content, and has no number and no ledger effect.</summary>
public sealed record CreditNoteVoided(CreditNoteId CreditNoteId, BillId BillId, Money Total, string Reason) : IDomainEvent, IHasReason;
