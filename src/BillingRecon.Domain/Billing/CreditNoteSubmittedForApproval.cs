using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>A draft credit note was handed to a second person (BR-212, BR-604). It has no number yet.</summary>
public sealed record CreditNoteSubmittedForApproval(CreditNoteId CreditNoteId, BillId BillId, Money Total) : IDomainEvent;
