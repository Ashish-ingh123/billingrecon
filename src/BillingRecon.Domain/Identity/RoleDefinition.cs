namespace BillingRecon.Domain.Identity;

/// <summary>
/// A row of the global <c>Roles</c> table (docs/database.md section 3.1): the code of a <see cref="Role"/> and its
/// name. Reference data, the same for every tenant, written by the migration and never by the application.
/// </summary>
public sealed class RoleDefinition
{
    /// <summary>Longest allowed code: the name of the longest <see cref="Role"/>, and room to spare.</summary>
    public const int CodeMaxLength = 30;

    /// <summary>Longest allowed <see cref="Name"/>.</summary>
    public const int NameMaxLength = 100;

    private RoleDefinition(Role code, string name)
    {
        Code = code;
        Name = name;
    }

    /// <summary>The five roles of R1, in the order of <see cref="Role"/>.</summary>
    public static IReadOnlyList<RoleDefinition> All { get; } =
    [
        new(Role.TenantAdmin, "Tenant administrator"),
        new(Role.Supervisor, "Supervisor"),
        new(Role.BillingClerk, "Billing clerk"),
        new(Role.Cashier, "Cashier"),
        new(Role.Viewer, "Viewer"),
    ];

    /// <summary>The role, stored by its name.</summary>
    public Role Code { get; private set; }

    /// <summary>How a person reads it.</summary>
    public string Name { get; private set; }
}
