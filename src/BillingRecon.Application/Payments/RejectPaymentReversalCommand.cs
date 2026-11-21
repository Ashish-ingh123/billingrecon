using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>A checker rejects the reversal of a payment, saying why; the payment stays as it was (BR-605).</summary>
public sealed record RejectPaymentReversalCommand(PaymentId Id, string ExpectedVersion, string Comment);
