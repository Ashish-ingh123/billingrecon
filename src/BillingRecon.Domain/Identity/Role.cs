namespace BillingRecon.Domain.Identity;

/// <summary>
/// What a user may do in a tenant (BR-601; docs/requirements.md section 1). A user holds one or more. The member names
/// are stored as text and are the codes of the <c>Roles</c> table, so renaming one is a data migration.
/// </summary>
public enum Role
{
    /// <summary>Runs the tenant: users, roles, settings and number-series prefixes, and everything else.</summary>
    TenantAdmin = 0,

    /// <summary>Works and approves: the checker of maker-checker requests (BR-603).</summary>
    Supervisor = 1,

    /// <summary>Bills, credit notes and adjustments.</summary>
    BillingClerk = 2,

    /// <summary>Payments, allocations and refunds.</summary>
    Cashier = 3,

    /// <summary>Reads and reports only.</summary>
    Viewer = 4,

    /// <summary>
    /// Reconciles: imports bank and gateway statements, matches them, records suspense cash and asks for it to be applied
    /// or returned (maker); reads documents and reports and makes the accountant's export. Approves nothing (ADR 0069).
    /// </summary>
    Accountant = 5,
}
