using BillingRecon.Domain.Billing;

namespace BillingRecon.Application.Billing;

/// <summary>
/// One application of a credit note's credit to a bill, as the API returns it (BR-212, BR-213). <c>AppliedAtPosting</c> is
/// true for the application the credit note made to its own bill when it was posted, which cannot be reversed;
/// <c>ReversedAtUtc</c> and <c>ReversalReason</c> are null unless the application was reversed (ADR 0028).
/// </summary>
public sealed record CreditNoteApplicationDto(
    Guid Id,
    Guid BillId,
    decimal Amount,
    CreditNoteAllocationStatus Status,
    DateTimeOffset AllocatedAtUtc,
    Guid? AllocatedByUserId,
    bool AppliedAtPosting,
    DateTimeOffset? ReversedAtUtc,
    string? ReversalReason);
