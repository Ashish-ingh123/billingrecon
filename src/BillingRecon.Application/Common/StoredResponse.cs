namespace BillingRecon.Application.Common;

/// <summary>
/// The answer to a request as the idempotency store keeps it and a replay repeats it (ADR 0006): the status, the
/// content type, the two headers a client acts on, and the body exactly as it was sent.
/// </summary>
public sealed record StoredResponse(int StatusCode, string? ContentType, string? Location, string? ETag, string Body);
