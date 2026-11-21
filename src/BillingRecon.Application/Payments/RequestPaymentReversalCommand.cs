using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>
/// A cashier asks for a payment to be reversed, saying why (US-33, BR-306). <c>ExpectedVersion</c> is the version of
/// the payment the cashier read (BR-804).
/// </summary>
public sealed record RequestPaymentReversalCommand(PaymentId Id, string ExpectedVersion, string Reason);
