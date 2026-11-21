using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>
/// A cashier asks for one allocation of a payment to be reversed, saying why (US-32, BR-305). <c>ExpectedVersion</c>
/// is the version of the payment the cashier read (BR-804).
/// </summary>
public sealed record RequestAllocationReversalCommand(PaymentId Id, PaymentAllocationId AllocationId, string ExpectedVersion, string Reason);
