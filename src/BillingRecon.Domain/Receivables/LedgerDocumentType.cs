namespace BillingRecon.Domain.Receivables;

/// <summary>The kind of document a posting belongs to (docs/database.md, section 3.7).</summary>
public enum LedgerDocumentType
{
    /// <summary>A bill.</summary>
    Bill = 0,

    /// <summary>A payment.</summary>
    Payment = 1,

    /// <summary>An allocation of a payment to a bill.</summary>
    PaymentAllocation = 2,

    /// <summary>A credit note.</summary>
    CreditNote = 3,

    /// <summary>An application of a credit note to a bill.</summary>
    CreditNoteAllocation = 4,

    /// <summary>An adjustment.</summary>
    Adjustment = 5,

    /// <summary>A refund.</summary>
    Refund = 6,
}
