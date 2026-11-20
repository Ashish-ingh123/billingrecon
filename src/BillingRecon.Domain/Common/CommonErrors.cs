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
}
