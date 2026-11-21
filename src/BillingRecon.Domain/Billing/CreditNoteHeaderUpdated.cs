using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>The date or the reason of a draft credit note was corrected. Raised only when a value really changed.</summary>
public sealed record CreditNoteHeaderUpdated(CreditNoteId CreditNoteId, DateOnly CreditNoteDate) : IDomainEvent;
