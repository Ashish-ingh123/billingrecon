namespace BillingRecon.Domain.Adjustments;

/// <summary>
/// Why a bill is lowered without money or a credit note (BR-501). The member names are stored as text
/// (docs/database.md, section 3.5), so renaming one is a data migration.
/// </summary>
public enum AdjustmentKind
{
    /// <summary>A reduction granted after the bill was posted.</summary>
    Discount,

    /// <summary>An amount that will not be collected.</summary>
    WriteOff,

    /// <summary>A difference of less than one rupee, settled so that the bill closes (BR-501).</summary>
    Rounding,
}
