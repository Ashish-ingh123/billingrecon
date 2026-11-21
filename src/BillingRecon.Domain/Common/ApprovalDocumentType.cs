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

    /// <summary>
    /// A change of the prefix of one document type (BR-802, ADR 0033). It was <c>NumberSeries</c> until Prompt 5.3b,
    /// when no request had used it: the request is about the change, not about a series.
    /// </summary>
    PrefixChange = 7,
}
