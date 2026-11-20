namespace BillingRecon.Domain.Payments;

/// <summary>
/// Where an allocation is in its life (docs/domain-model.md, section 4.6). The states of a reversal arrive with
/// reversals (BR-305), at the end of this enum.
/// </summary>
public enum PaymentAllocationStatus
{
    /// <summary>In force: the amount is applied to the bill.</summary>
    Active = 0,
}
