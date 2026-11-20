namespace BillingRecon.Domain.Common;

/// <summary>The kind of document an approval request is about (BR-604; docs/database.md, section 3.6).</summary>
public enum ApprovalDocumentType
{
    /// <summary>A bill at or above the posting threshold of the tenant (BR-207).</summary>
    Bill = 0,

    /// <summary>A credit note.</summary>
    CreditNote = 1,

    /// <summary>An adjustment.</summary>
    Adjustment = 2,

    /// <summary>A refund.</summary>
    Refund = 3,

    /// <summary>A payment, when it is reversed.</summary>
    Payment = 4,

    /// <summary>An allocation of a payment, when it is reversed.</summary>
    PaymentAllocation = 5,

    /// <summary>An application of a credit note, when it is reversed.</summary>
    CreditNoteAllocation = 6,

    /// <summary>A number series, when its prefix is changed (BR-802).</summary>
    NumberSeries = 7,
}
