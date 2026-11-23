namespace BillingRecon.Application.Reporting;

/// <summary>
/// How money was received or paid back, in a report. Reporting's own word for it, since it may not name the Payments
/// module's enum (ADR 0036); the names are the same, and a test keeps them so.
/// </summary>
public enum ReportPaymentMethod
{
    /// <summary>Cash.</summary>
    Cash = 0,

    /// <summary>A card.</summary>
    Card = 1,

    /// <summary>UPI.</summary>
    UPI = 2,

    /// <summary>A bank transfer.</summary>
    BankTransfer = 3,

    /// <summary>A cheque.</summary>
    Cheque = 4,
}
