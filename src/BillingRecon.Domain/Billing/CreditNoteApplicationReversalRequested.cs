using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// A cashier asked for one application of a credit note to be reversed (ADR 0028). <c>DocumentNumber</c> is the
/// number of the credit note. Nothing financial has happened yet.
/// </summary>
public sealed record CreditNoteApplicationReversalRequested(string DocumentNumber, CreditNoteAllocationId ApplicationId, BillId BillId, Money Amount, string Reason)
    : IDomainEvent, IHasDocumentNumber, IHasReason;
