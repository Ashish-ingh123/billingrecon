namespace BillingRecon.Domain.Identity;

/// <summary>The paper a document is printed on (ADR 0059).</summary>
public enum PaperSize
{
    /// <summary>A4, 210 by 297 mm.</summary>
    A4 = 1,

    /// <summary>A5, 148 by 210 mm.</summary>
    A5 = 2,

    /// <summary>A roll 80 mm wide, for a receipt or a refund voucher; never for a bill.</summary>
    Roll80 = 3,
}
