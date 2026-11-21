using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>
/// The business failures of the Identity module: one code constant and one factory per failure, so that a code is
/// written once, cited from the tests by name, and listed in the API contract (docs/api.md). Every module keeps its
/// own <c>&lt;Module&gt;Errors</c> class in this shape; a test checks that the codes are snake_case and unique.
/// </summary>
public static class IdentityErrors
{
    /// <summary>Code of <see cref="TenantInvalid"/>.</summary>
    public const string TenantInvalidCode = "tenant_invalid";

    /// <summary>Code of <see cref="UserNotFound"/>.</summary>
    public const string UserNotFoundCode = "user_not_found";

    /// <summary>Code of <see cref="UserEmailTaken"/>.</summary>
    public const string UserEmailTakenCode = "user_email_taken";

    /// <summary>Code of <see cref="UserAlreadyDisabled"/>.</summary>
    public const string UserAlreadyDisabledCode = "user_already_disabled";

    /// <summary>Code of <see cref="UserAlreadyActive"/>.</summary>
    public const string UserAlreadyActiveCode = "user_already_active";

    /// <summary>Code of <see cref="UserLastTenantAdmin"/>.</summary>
    public const string UserLastTenantAdminCode = "user_last_tenant_admin";

    /// <summary>Code of <see cref="UserCannotDisableSelf"/>.</summary>
    public const string UserCannotDisableSelfCode = "user_cannot_disable_self";

    /// <summary>The code or the name of a tenant is not valid; <paramref name="fieldErrors"/> says which, per field.</summary>
    public static Error TenantInvalid(IReadOnlyDictionary<string, string[]> fieldErrors)
        => Error.Validation(TenantInvalidCode, "The tenant is not valid.", fieldErrors);

    /// <summary>No user with that id exists for the tenant. Another tenant's user gets the same answer.</summary>
    public static Error UserNotFound() => Error.NotFound(UserNotFoundCode, "The user does not exist.");

    /// <summary>Another user of the tenant has the e-mail address, compared without regard to case (BR-601).</summary>
    public static Error UserEmailTaken()
        => Error.Conflict(UserEmailTakenCode, "Another user of this hospital already has this e-mail address (BR-601).");

    /// <summary>Disabling a user who is disabled already.</summary>
    public static Error UserAlreadyDisabled() => Error.BusinessRule(UserAlreadyDisabledCode, "The user is already disabled.");

    /// <summary>Enabling a user who is active already.</summary>
    public static Error UserAlreadyActive() => Error.BusinessRule(UserAlreadyActiveCode, "The user is already active.");

    /// <summary>
    /// Disabling the last active tenant administrator, or taking the role away from them: nobody would be left to
    /// manage the users of the tenant (ADR 0030).
    /// </summary>
    public static Error UserLastTenantAdmin()
        => Error.BusinessRule(UserLastTenantAdminCode, "This is the last active tenant administrator; make another user a TenantAdmin first.");

    /// <summary>A user disabling themselves (ADR 0030).</summary>
    public static Error UserCannotDisableSelf()
        => Error.BusinessRule(UserCannotDisableSelfCode, "You cannot disable your own user; another administrator can.");
}
