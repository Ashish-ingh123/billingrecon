namespace BillingRecon.Domain.Reconciliation;

/// <summary>
/// The gateways whose settlement report BillingRecon reads (BR-1019; ADR 0068). Stored as text, so renaming one is a
/// data migration. A new gateway is a new member and a new reader of the same model.
/// </summary>
public enum PaymentGateway
{
    /// <summary>Razorpay's settlement report.</summary>
    Razorpay,
}
