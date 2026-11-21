using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>The lines of a draft credit note were replaced, and with them its totals. Raised only when a line really changed.</summary>
public sealed record CreditNoteLinesReplaced(CreditNoteId CreditNoteId, int LineCount, Money Total) : IDomainEvent;
