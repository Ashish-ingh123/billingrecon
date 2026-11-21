using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// An application of credit was reversed (ADR 0028): its amount is available credit again. <c>AvailableAmount</c> is
/// what the credit note has available afterwards.
/// </summary>
public sealed record CreditNoteApplicationReversed(string DocumentNumber, CreditNoteAllocationId ApplicationId, BillId BillId, Money Amount, Money AvailableAmount, string Reason)
    : IDomainEvent, IHasDocumentNumber, IHasReason;
