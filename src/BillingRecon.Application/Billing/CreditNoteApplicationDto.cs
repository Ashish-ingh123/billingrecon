using BillingRecon.Domain.Billing;

namespace BillingRecon.Application.Billing;

/// <summary>One application of a credit note's credit to a bill, as the API returns it (BR-212, BR-213).</summary>
public sealed record CreditNoteApplicationDto(
    Guid Id,
    Guid BillId,
    decimal Amount,
    CreditNoteAllocationStatus Status,
    DateTimeOffset AllocatedAtUtc,
    Guid? AllocatedByUserId);
