namespace BillingRecon.Application.Reporting;

/// <summary>
/// The state of a payment in a report. Reporting's own word for it, since it may not name the Payments module's enum
/// (ADR 0036); the names are the same, and a test keeps them so.
/// </summary>
public enum ReportPaymentStatus
{
    /// <summary>Recorded and posted.</summary>
    Posted = 0,

    /// <summary>Its reversal waits for a second person.</summary>
    PendingReversal = 1,

    /// <summary>Reversed: it holds nothing any more.</summary>
    Reversed = 2,
}
