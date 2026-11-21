using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>
/// A checker approves the reversal of an allocation, which reverses it (BR-305, BR-606). <c>ExpectedVersion</c> is the
/// version of the payment the checker read (BR-804); <c>Comment</c> is optional.
/// </summary>
public sealed record ApproveAllocationReversalCommand(PaymentId Id, PaymentAllocationId AllocationId, string ExpectedVersion, string? Comment);
