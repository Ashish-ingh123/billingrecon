using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>One allocation of a payment to a bill, as the API returns it. <c>ReversedAtUtc</c> and <c>ReversalReason</c> are
/// null unless the allocation was reversed (BR-305).</summary>
public sealed record PaymentAllocationDto(
    Guid Id,
    Guid BillId,
    decimal Amount,
    PaymentAllocationStatus Status,
    DateTimeOffset AllocatedAtUtc,
    Guid? AllocatedByUserId,
    DateTimeOffset? ReversedAtUtc,
    string? ReversalReason);
