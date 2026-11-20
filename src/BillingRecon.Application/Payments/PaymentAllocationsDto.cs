namespace BillingRecon.Application.Payments;

/// <summary>
/// A payment with every allocation made from it, the oldest first: what an allocation request answers and what
/// <c>GET /payments/{id}/allocations</c> returns. The version of the payment is the ETag.
/// </summary>
public sealed record PaymentAllocationsDto(PaymentDto Payment, IReadOnlyList<PaymentAllocationDto> Allocations);
