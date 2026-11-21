namespace BillingRecon.Domain.Billing;

/// <summary>
/// Where the application of a credit note to a bill is in its life (docs/domain-model.md, section 4.5). The states of
/// a reversal are not built: no prompt of R1 reverses an application yet.
/// </summary>
public enum CreditNoteAllocationStatus
{
    /// <summary>In force: the amount is applied to the bill.</summary>
    Active = 0,
}
