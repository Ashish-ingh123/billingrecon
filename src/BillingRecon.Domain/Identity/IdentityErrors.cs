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

    /// <summary>Code of <see cref="InvalidCredentials"/>.</summary>
    public const string InvalidCredentialsCode = "invalid_credentials";

    /// <summary>Code of <see cref="PasswordCurrentWrong"/>.</summary>
    public const string PasswordCurrentWrongCode = "password_current_wrong";

    /// <summary>Code of <see cref="PasswordChangeRequired"/>.</summary>
    public const string PasswordChangeRequiredCode = "password_change_required";

    /// <summary>Code of <see cref="PrefixChangeNotFound"/>.</summary>
    public const string PrefixChangeNotFoundCode = "prefix_change_not_found";

    /// <summary>Code of <see cref="PrefixChangeNotPending"/>.</summary>
    public const string PrefixChangeNotPendingCode = "prefix_change_not_pending";

    /// <summary>Code of <see cref="PrefixChangePending"/>.</summary>
    public const string PrefixChangePendingCode = "prefix_change_pending";

    /// <summary>Code of <see cref="PrefixUnchanged"/>.</summary>
    public const string PrefixUnchangedCode = "prefix_unchanged";

    /// <summary>Code of <see cref="PrefixInUse"/>.</summary>
    public const string PrefixInUseCode = "prefix_in_use";

    /// <summary>
    /// A sign-in that is refused, whatever the reason: an unknown or suspended tenant, an unknown address, a wrong
    /// password, a disabled user (BR-601; ADR 0031). One answer for all, so that nobody learns which accounts exist.
    /// </summary>
    public static Error InvalidCredentials()
        => Error.Unauthorized(InvalidCredentialsCode, "The hospital code, the e-mail address or the password is not right, or the user may not sign in.");

    /// <summary>The current password given to change one's own password is not the user's (ADR 0051).</summary>
    public static Error PasswordCurrentWrong()
        => Error.BusinessRule(PasswordCurrentWrongCode, "The current password is not right; nothing was changed.");

    /// <summary>
    /// The user must set a password of their own before anything else (ADR 0051): every endpoint but the change of one's
    /// own password answers this while the change is due.
    /// </summary>
    public static Error PasswordChangeRequired()
        => Error.Forbidden(PasswordChangeRequiredCode, "Set a password of your own first: the one you were given must be changed.");

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

    /// <summary>No prefix change with that id exists for the tenant. Another tenant's gets the same answer.</summary>
    public static Error PrefixChangeNotFound() => Error.NotFound(PrefixChangeNotFoundCode, "The prefix change does not exist.");

    /// <summary>Deciding a prefix change that was decided already (BR-605).</summary>
    public static Error PrefixChangeNotPending()
        => Error.BusinessRule(PrefixChangeNotPendingCode, "The prefix change has been decided already; a decision is final.");

    /// <summary>Asking for a second prefix change of a document type while one waits (ADR 0033).</summary>
    public static Error PrefixChangePending()
        => Error.Conflict(PrefixChangePendingCode, "A change of the prefix of this document type already waits for a decision.");

    /// <summary>Asking for the prefix the document type already has.</summary>
    public static Error PrefixUnchanged() => Error.BusinessRule(PrefixUnchangedCode, "The document type has this prefix already.");

    /// <summary>
    /// A prefix that another document type has, had, or waits to get: a prefix belongs to one kind of document for good,
    /// so that two documents of different kinds never carry the same number (BR-802, ADR 0033).
    /// </summary>
    public static Error PrefixInUse()
        => Error.Conflict(PrefixInUseCode, "This prefix belongs to another kind of document, now or in the past; choose another one (BR-802).");
}
