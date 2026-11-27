namespace BillingRecon.Domain.Payments;

/// <summary>Where a cashier's shift stands (BR-310 to BR-315; ADR 0060).</summary>
public enum CashierShiftStatus
{
    /// <summary>Open: cash is taken and paid out in it.</summary>
    Open = 0,

    /// <summary>Counted with a difference, waiting for a checker (BR-314). No cash joins it.</summary>
    PendingApproval = 1,

    /// <summary>Closed, for good (BR-315).</summary>
    Closed = 2,
}
