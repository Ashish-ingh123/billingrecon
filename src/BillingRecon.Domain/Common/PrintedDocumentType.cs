namespace BillingRecon.Domain.Common;

/// <summary>The kinds of document that are printed for a patient or a payer (ADR 0054). The member names are stored as text.</summary>
public enum PrintedDocumentType
{
    /// <summary>A posted bill: a tax invoice, a bill of supply, or the statement of an opening balance.</summary>
    Bill = 0,

    /// <summary>A payment: its receipt.</summary>
    Payment = 1,

    /// <summary>A posted refund: its voucher.</summary>
    Refund = 2,
}
