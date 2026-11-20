namespace BillingRecon.Domain.Billing;

/// <summary>
/// Whether an entry of the service catalogue can be put on new bill lines. The member names are stored as text
/// (docs/database.md, section 3.3), so renaming one is a data migration.
/// </summary>
public enum ServiceItemStatus
{
    /// <summary>The item can be chosen for a new bill line.</summary>
    Active,

    /// <summary>The item is no longer offered for new bill lines; lines that already name it keep their own price and tax rate.</summary>
    Inactive,
}
