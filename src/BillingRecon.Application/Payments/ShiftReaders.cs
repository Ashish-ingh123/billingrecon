using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Payments;

/// <summary>
/// Who reads every shift of the hospital, and who only their own (the permission matrix, docs/requirements.md
/// section 1; ADR 0060): a TenantAdmin, a Supervisor, a Viewer and an Accountant (ADR 0069) read all; a Cashier and a BillingClerk read the
/// shifts they opened. The shift of another cashier is 404 to them, as the record of another tenant is.
/// </summary>
public static class ShiftReaders
{
    /// <summary>True when a user with <paramref name="roles"/> reads every shift.</summary>
    public static bool SeeAll(IReadOnlyCollection<Role> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        return roles.Contains(Role.TenantAdmin) || roles.Contains(Role.Supervisor) || roles.Contains(Role.Viewer) || roles.Contains(Role.Accountant);
    }
}
