using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>The checker rejected the reversal of an application of credit (BR-605): the application is in force as before.</summary>
public sealed record CreditNoteApplicationReversalRejected(string DocumentNumber, CreditNoteAllocationId ApplicationId, string Reason)
    : IDomainEvent, IHasDocumentNumber, IHasReason;
