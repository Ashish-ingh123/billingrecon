using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>The checker rejected a credit note that waited for approval (BR-605): it is a draft again.</summary>
public sealed record CreditNoteReturnedToDraft(CreditNoteId CreditNoteId, string Reason) : IDomainEvent, IHasReason;
