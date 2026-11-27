
namespace BillingRecon.Application.Reporting;

/// <summary>The type of voucher a posting becomes in the accountant's package (ADR 0062).</summary>
public enum ExportVoucherType
{
    /// <summary>Money received: a payment recorded.</summary>
    Receipt = 0,

    /// <summary>Money paid out: a refund, or a payment reversed.</summary>
    Payment = 1,

    /// <summary>Everything that moves no money: bills, credit notes, allocations, adjustments, opening balances.</summary>
    Journal = 2,
}
