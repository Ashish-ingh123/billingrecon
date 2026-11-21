using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>A checker rejects the reversal of an allocation, saying why; the allocation stays in force (BR-605).</summary>
public sealed record RejectAllocationReversalCommand(PaymentId Id, PaymentAllocationId AllocationId, string ExpectedVersion, string Comment);
