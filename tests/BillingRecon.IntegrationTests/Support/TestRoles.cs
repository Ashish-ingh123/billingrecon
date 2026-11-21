using BillingRecon.Domain.Identity;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// The roles a test client names, as roles: the names that are roles, in their order, or a tenant administrator when
/// none is. The token of the client and the user row behind it (<c>TestUsers</c>) get the same, as a sign-in would.
/// </summary>
internal static class TestRoles
{
    /// <summary>The roles of <paramref name="names"/>.</summary>
    public static IReadOnlyList<Role> Of(IEnumerable<string> names)
    {
        Role[] roles = [.. names.Select(name => Enum.TryParse<Role>(name, out var role) ? role : (Role?)null).OfType<Role>()];

        return roles.Length > 0 ? roles : [Role.TenantAdmin];
    }
}
