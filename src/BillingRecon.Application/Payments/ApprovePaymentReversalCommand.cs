using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>
/// A checker approves the reversal of a payment, which reverses its allocations and then the payment (BR-306,
/// BR-606). <c>ExpectedVersion</c> is the version the checker read (BR-804); <c>Comment</c> is optional.
/// </summary>
public sealed record ApprovePaymentReversalCommand(PaymentId Id, string ExpectedVersion, string? Comment);
