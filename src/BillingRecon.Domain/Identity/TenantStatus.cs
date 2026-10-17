namespace BillingRecon.Domain.Identity;

/// <summary>
/// Whether a tenant may use the system. The member names are stored as text (docs/database.md, section 3.1), so
/// renaming one is a data migration.
/// </summary>
public enum TenantStatus
{
    /// <summary>The tenant works normally.</summary>
    Active,

    /// <summary>The tenant is locked out. Its data is kept.</summary>
    Suspended,
}
