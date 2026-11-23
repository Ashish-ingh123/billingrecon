namespace BillingRecon.Application.Reporting;

/// <summary>
/// The state of a bill in the bill register: one of the states of a posted bill (BR-208). Reporting's own word for it,
/// since it may not name the Billing module's enum (ADR 0036); the names are the same, and a test keeps them so.
/// </summary>
public enum ReportBillStatus
{
    /// <summary>Posted, nothing settled yet.</summary>
    Posted = 0,

    /// <summary>Posted, part of it settled.</summary>
    PartiallyPaid = 1,

    /// <summary>Posted and settled in full.</summary>
    Settled = 2,
}
