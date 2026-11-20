namespace BillingRecon.Domain.Payments;

/// <summary>How money was received (BR-301). The member names are stored as text, so renaming one is a data migration.</summary>
public enum PaymentMethod
{
    /// <summary>Cash over the counter. The only method that needs no reference.</summary>
    Cash = 0,

    /// <summary>A debit or credit card; the reference is the authorisation code.</summary>
    Card = 1,

    /// <summary>Unified Payments Interface; the reference is the UPI transaction id.</summary>
    UPI = 2,

    /// <summary>NEFT, RTGS or IMPS; the reference is the UTR.</summary>
    BankTransfer = 3,

    /// <summary>A cheque; the reference is the cheque number.</summary>
    Cheque = 4,
}
