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

    /// <summary>Code of <see cref="SessionEnded"/>.</summary>
    public const string SessionEndedCode = "session_ended";

    /// <summary>Code of <see cref="PrefixChangeNotFound"/>.</summary>
    public const string PrefixChangeNotFoundCode = "prefix_change_not_found";

    /// <summary>Code of <see cref="LetterheadImageNotFound"/>.</summary>
    public const string LetterheadImageNotFoundCode = "letterhead_image_not_found";

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

    /// <summary>Code of <see cref="TenantSuspended"/>.</summary>
    public const string TenantSuspendedCode = "tenant_suspended";

    /// <summary>Code of <see cref="TenantCodeTaken"/>.</summary>
    public const string TenantCodeTakenCode = "tenant_code_taken";

    /// <summary>Code of <see cref="TenantNotFound"/>.</summary>
    public const string TenantNotFoundCode = "tenant_not_found";

    /// <summary>Code of <see cref="TenantStatusUnchanged"/>.</summary>
    public const string TenantStatusUnchangedCode = "tenant_status_unchanged";

    /// <summary>The hospital of the request is suspended (ADR 0052): nothing is answered until the operator lifts it.</summary>
    public static Error TenantSuspended()
        => Error.Forbidden(TenantSuspendedCode, "This hospital is suspended; ask the operator of BillingRecon.");

    /// <summary>The onboarding command was given the code of a tenant that exists (ADR 0052).</summary>
    public static Error TenantCodeTaken()
        => Error.Conflict(TenantCodeTakenCode, "A hospital with this code exists already; nothing was created.");

    /// <summary>No tenant has the code an operator's command was given (ADR 0052).</summary>
    public static Error TenantNotFound() => Error.NotFound(TenantNotFoundCode, "No hospital has this code.");

    /// <summary>The tenant is suspended already, or active already (ADR 0052).</summary>
    public static Error TenantStatusUnchanged(TenantStatus status)
        => Error.BusinessRule(TenantStatusUnchangedCode, $"The hospital is {status.ToString().ToLowerInvariant()} already; nothing was changed.");

    /// <summary>
    /// The user must set a password of their own before anything else (ADR 0051): every endpoint but the change of one's
    /// own password answers this while the change is due.
    /// </summary>
    public static Error PasswordChangeRequired()
        => Error.Forbidden(PasswordChangeRequiredCode, "Set a password of your own first: the one you were given must be changed.");

    /// <summary>
    /// The token of the request was issued for a session that has ended (ADR 0057): the user signed out, the password or the
    /// roles changed, or the user was disabled. Signing in again gives a token that works.
    /// </summary>
    public static Error SessionEnded()
        => Error.Unauthorized(SessionEndedCode, "This session has ended. Sign in again.");

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

    /// <summary>The hospital has no logo, or no signature, on its letterhead (ADR 0059).</summary>
    public static Error LetterheadImageNotFound() => Error.NotFound(LetterheadImageNotFoundCode, "The letterhead has no such image.");

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
    /// <summary>Code of <see cref="SecondStepCodeWrong"/>.</summary>
    public const string SecondStepCodeWrongCode = "second_step_code_wrong";

    /// <summary>Code of <see cref="SecondStepNotEnrolled"/>.</summary>
    public const string SecondStepNotEnrolledCode = "second_step_not_enrolled";

    /// <summary>Code of <see cref="SecondStepAlreadyEnrolled"/>.</summary>
    public const string SecondStepAlreadyEnrolledCode = "second_step_already_enrolled";

    /// <summary>Code of <see cref="SecondStepEnrolmentNotBegun"/>.</summary>
    public const string SecondStepEnrolmentNotBegunCode = "second_step_enrolment_not_begun";

    /// <summary>Code of <see cref="SecondStepRequired"/>.</summary>
    public const string SecondStepRequiredCode = "second_step_required";

    /// <summary>Code of <see cref="SecondStepEnrolmentRequired"/>.</summary>
    public const string SecondStepEnrolmentRequiredCode = "second_step_enrolment_required";

    /// <summary>Code of <see cref="ApprovalOnHold"/>.</summary>
    public const string ApprovalOnHoldCode = "approval_on_hold";

    /// <summary>The code of the second step is not the one expected, was used already, or is no recovery code of the user.</summary>
    public static Error SecondStepCodeWrong()
        => Error.BusinessRule(SecondStepCodeWrongCode, "The code is not right. Give the code your authenticator app shows now, or a recovery code you have not used.");

    /// <summary>A code was given by a user who has no second step.</summary>
    public static Error SecondStepNotEnrolled()
        => Error.BusinessRule(SecondStepNotEnrolledCode, "You have no second step to give a code for.");

    /// <summary>A user who has a second step began to enrol one.</summary>
    public static Error SecondStepAlreadyEnrolled()
        => Error.BusinessRule(SecondStepAlreadyEnrolledCode, "You have a second step already. An administrator can take it away if you lost it.");

    /// <summary>An enrolment was confirmed that was never begun.</summary>
    public static Error SecondStepEnrolmentNotBegun()
        => Error.BusinessRule(SecondStepEnrolmentNotBegunCode, "Begin the enrolment first: there is no secret to confirm.");

    /// <summary>The request needs a sign-in whose second step was given, and this one stopped after the password, or has none.</summary>
    public static Error SecondStepRequired()
        => Error.Forbidden(SecondStepRequiredCode, "This needs the second step of your sign-in: give the code of your authenticator app first.");

    /// <summary>The user's roles need a second step and the user has none: only the enrolment is open.</summary>
    public static Error SecondStepEnrolmentRequired()
        => Error.Forbidden(SecondStepEnrolmentRequiredCode, "Your role needs a second step at sign-in. Set it up first.");

    /// <summary>An administrator reset the user's password or second step less than 24 hours ago: the user decides nothing until then.</summary>
    public static Error ApprovalOnHold(DateTimeOffset untilUtc)
        => Error.Forbidden(ApprovalOnHoldCode, $"Your password or second step was reset by an administrator: you can approve and reject again after {untilUtc:yyyy-MM-dd HH:mm} UTC.");
}
