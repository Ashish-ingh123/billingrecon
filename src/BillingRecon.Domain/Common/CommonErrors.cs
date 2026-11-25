namespace BillingRecon.Domain.Common;

/// <summary>
/// The failures any use case can report, whatever its module: fields that are invalid, a write that lost a race to
/// another one (BR-804), and a row that a unique index refused. The codes are part of the API contract (docs/api.md).
/// </summary>
public static class CommonErrors
{
    /// <summary>One or more fields of the request are invalid. The error lists the messages per field.</summary>
    public const string ValidationFailedCode = "validation_failed";

    /// <summary>The record was changed after the caller read it (BR-804, ADR 0007).</summary>
    public const string ConcurrencyConflictCode = "concurrency_conflict";

    /// <summary>
    /// A unique index refused the row. A use case that knows which rule the index enforces replaces this error with the
    /// error of that rule, so a client sees it only where no use case could tell.
    /// </summary>
    public const string DuplicateKeyCode = "duplicate_key";

    /// <summary>A financial POST came without an <c>Idempotency-Key</c> header, or with one that is not a single UUID (BR-803).</summary>
    public const string IdempotencyKeyMissingCode = "idempotency_key_missing";

    /// <summary>The first request with this idempotency key is still running (BR-803).</summary>
    public const string IdempotencyInProgressCode = "idempotency_in_progress";

    /// <summary>The idempotency key was used before, for a different request (BR-803).</summary>
    public const string IdempotencyKeyReuseCode = "idempotency_key_reuse";

    public const string ApprovalCheckerIsMakerCode = "approval_checker_is_maker";

    public const string ApprovalAlreadyDecidedCode = "approval_already_decided";

    public const string ApprovalNeedsUserCode = "approval_needs_user";

    public const string ApprovalCheckerRoleCode = "approval_checker_role";

    /// <summary>Code of <see cref="DocumentNotPrintable"/>.</summary>
    public const string DocumentNotPrintableCode = "document_not_printable";

    /// <summary>The request has invalid fields; <paramref name="fieldErrors"/> holds the messages per field.</summary>
    public static Error ValidationFailed(IReadOnlyDictionary<string, string[]> fieldErrors)
        => Error.Validation(ValidationFailedCode, "The request is not valid. The errors are listed per field.", fieldErrors);

    /// <summary>The record changed since the caller read it.</summary>
    public static Error ConcurrencyConflict()
        => Error.Conflict(
            ConcurrencyConflictCode,
            "The record was changed by someone else after you read it. Read it again, then retry (BR-804).");

    /// <summary>A unique index refused the row.</summary>
    public static Error DuplicateKey()
        => Error.Conflict(DuplicateKeyCode, "The record conflicts with one that already exists.");

    /// <summary>The <c>Idempotency-Key</c> header is missing or is not one UUID.</summary>
    public static Error IdempotencyKeyMissing()
        => Error.Validation(
            IdempotencyKeyMissingCode,
            "This request needs an Idempotency-Key header: one UUID that you generate for the operation and send again unchanged when you retry it (BR-803).",
            new Dictionary<string, string[]> { ["Idempotency-Key"] = ["Send one UUID, for example 3f2504e0-4f89-11d3-9a0c-0305e82c3301. The empty UUID is not a key."] });

    /// <summary>The first request with the key has not been answered yet.</summary>
    public static Error IdempotencyInProgress()
        => Error.Conflict(
            IdempotencyInProgressCode,
            "A request with this Idempotency-Key is still being processed. Wait, then send the same request with the same key again (BR-803).");

    /// <summary>The key belongs to a different request.</summary>
    public static Error IdempotencyKeyReuse()
        => Error.BusinessRule(
            IdempotencyKeyReuseCode,
            "This Idempotency-Key was already used for a different request. A key belongs to one operation: generate a new key for a new one, also when you try again after a refusal (BR-803).");
    /// <summary>
    /// The user who asked for an action tried to approve or reject it themselves (BR-603). The checker is always a
    /// different user, whatever the roles.
    /// </summary>
    public static Error ApprovalCheckerIsMaker()
        => Error.Forbidden(ApprovalCheckerIsMakerCode, "You asked for this yourself, so you cannot decide it: a second person has to approve or reject it (BR-603).");

    /// <summary>An approval request that a checker has already approved or rejected: a decision is final (BR-605).</summary>
    public static Error ApprovalAlreadyDecided()
        => Error.Conflict(ApprovalAlreadyDecidedCode, "This request has already been decided. Read the document again.");

    /// <summary>
    /// An action that needs a maker or a checker was asked for without a user. System work cannot ask for an approval
    /// or give one: both steps belong to a person (BR-603).
    /// </summary>
    public static Error ApprovalNeedsUser()
        => Error.Forbidden(ApprovalNeedsUserCode, "This action needs a signed-in user: an approval is asked for and given by people (BR-603).");

    /// <summary>
    /// A decision by a user who holds neither Supervisor nor TenantAdmin (BR-603, ADR 0032). The API's policy refuses
    /// such a user first; this is the decision's own check, which no endpoint can leave out.
    /// </summary>
    public static Error ApprovalCheckerRole()
        => Error.Forbidden(ApprovalCheckerRoleCode, "Only a Supervisor or a TenantAdmin may approve or reject (BR-603).");

    /// <summary>
    /// Printing a document that has not taken financial effect, or no longer has (ADR 0054): a draft, one that waits for
    /// approval, a voided one, a reversed payment. Only a posted document is handed to a patient.
    /// </summary>
    public static Error DocumentNotPrintable()
        => Error.BusinessRule(DocumentNotPrintableCode, "Only a posted document is printed: this one is a draft, waits for approval, or was voided or reversed.");
}
