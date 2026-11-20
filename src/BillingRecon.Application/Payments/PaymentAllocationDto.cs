using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>One allocation of a payment to a bill, as the API returns it.</summary>
public sealed record PaymentAllocationDto(
    Guid Id,
    Guid BillId,
    decimal Amount,
    PaymentAllocationStatus Status,
    DateTimeOffset AllocatedAtUtc,
    Guid? AllocatedByUserId);
