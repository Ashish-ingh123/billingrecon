namespace BillingRecon.Domain.Payments;

/// <summary>
/// Where a payment is in its life (docs/domain-model.md, section 5.3). A payment is posted the moment it is recorded
/// (BR-302), so there is no draft. The states of a reversal arrive with reversals, at the end of this enum.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Recorded: it has its number and its ledger entry.</summary>
    Posted = 0,
}
