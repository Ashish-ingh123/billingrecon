namespace BillingRecon.Domain.Common;

/// <summary>
/// The record of one idempotency key of one tenant (BR-803, ADR 0006): the request that first used it, as a
/// fingerprint, and, once that request has been answered, the answer, so that a retry gets the same one instead of
/// doing the work twice. The key identifies an operation, not a user: two users of one tenant cannot use one key for
/// two operations.
/// <para>
/// The row is identified by the tenant and the key together and has no id of its own. It is one of the two kinds of
/// row the application may delete (<see cref="IHardDeletable"/>): a reservation whose request failed with a fault is
/// removed so that the client can try again, and keys older than their expiry are swept. It is a record of requests,
/// not a financial record (CLAUDE.md section 4, rule 10).
/// </para>
/// </summary>
public sealed class IdempotencyKey : ITenantScoped, IHardDeletable
{
    /// <summary>How long a key is remembered (BR-803).</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    /// <summary>
    /// How long a reservation may wait for its answer before it is taken for abandoned (review 2026-11, finding R3):
    /// the process that reserved it died before it answered. No request runs that long: a command of the database
    /// times out long before.
    /// </summary>
    public static readonly TimeSpan AbandonedAfter = TimeSpan.FromMinutes(5);

    /// <summary>Longest stored <see cref="Method"/>.</summary>
    public const int MethodMaxLength = 8;

    /// <summary>Longest stored <see cref="Path"/>; a longer path is cut for storage, the fingerprint covers all of it.</summary>
    public const int PathMaxLength = 400;

    /// <summary>The length of <see cref="RequestHash"/>: a SHA-256 hash.</summary>
    public const int RequestHashLength = 32;

    /// <summary>Longest stored <see cref="ResponseContentType"/>.</summary>
    public const int ResponseContentTypeMaxLength = 100;

    /// <summary>Longest stored <see cref="ResponseHeaders"/>.</summary>
    public const int ResponseHeadersMaxLength = 1000;

    // Also the constructor Entity Framework uses to load a row; it then sets the remaining properties.
    private IdempotencyKey(string method, string path)
    {
        Method = method;
        Path = path;
        RequestHash = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The key the client chose: a UUID, unique within the tenant.</summary>
    public Guid Key { get; private set; }

    /// <summary>Who sent the first request; null when no user did.</summary>
    public UserId? UserId { get; private set; }

    /// <summary>The HTTP method of the first request.</summary>
    public string Method { get; private set; }

    /// <summary>The path of the first request, for whoever reads the table; cut to <see cref="PathMaxLength"/>.</summary>
    public string Path { get; private set; }

    /// <summary>The fingerprint of the first request. A later request with the key must have the same one.</summary>
    public byte[] RequestHash { get; private set; }

    /// <summary>Whether the first request is still running or has been answered.</summary>
    public IdempotencyStatus Status { get; private set; }

    /// <summary>The HTTP status of the answer; null until there is one.</summary>
    public int? StatusCode { get; private set; }

    /// <summary>The content type of the answer.</summary>
    public string? ResponseContentType { get; private set; }

    /// <summary>The headers of the answer that a replay repeats (<c>Location</c>, <c>ETag</c>), as JSON.</summary>
    public string? ResponseHeaders { get; private set; }

    /// <summary>The body of the answer, exactly as it was sent.</summary>
    public string? ResponseBody { get; private set; }

    /// <summary>When the key was reserved.</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>When the answer was stored; null until then.</summary>
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>When the key may be forgotten: <see cref="Retention"/> after it was reserved.</summary>
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    /// <summary>A reservation of <paramref name="key"/> for a request with the fingerprint <paramref name="requestHash"/>, in progress.</summary>
    /// <exception cref="ArgumentException">The key is the empty UUID, or the hash is not a SHA-256 hash.</exception>
    public static IdempotencyKey Reserve(Guid key, UserId? userId, string method, string path, byte[] requestHash, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(requestHash);

        if (key == Guid.Empty)
        {
            throw new ArgumentException("An idempotency key is a UUID; the empty one is not a key.", nameof(key));
        }

        if (requestHash.Length != RequestHashLength)
        {
            throw new ArgumentException($"The fingerprint of a request is a SHA-256 hash of {RequestHashLength} bytes.", nameof(requestHash));
        }

        return new IdempotencyKey(Cut(method, MethodMaxLength), Cut(path, PathMaxLength))
        {
            Key = key,
            UserId = userId,
            RequestHash = [.. requestHash],
            Status = IdempotencyStatus.InProgress,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc + Retention,
        };
    }

    /// <summary>Whether <paramref name="requestHash"/> is the fingerprint of the request that reserved the key.</summary>
    public bool IsFor(ReadOnlySpan<byte> requestHash) => requestHash.SequenceEqual(RequestHash);

    /// <summary>
    /// Whether the key was reserved, never answered, and reserved longer ago than <see cref="AbandonedAfter"/>: the
    /// same request may then reserve it anew.
    /// </summary>
    /// <param name="nowUtc">The instant of the question.</param>
    /// <returns>True for a reservation that nothing will answer any more.</returns>
    public bool IsAbandoned(DateTimeOffset nowUtc) => Status == IdempotencyStatus.InProgress && nowUtc - CreatedAtUtc > AbandonedAfter;

    /// <summary>Stores the answer to the first request. A key is completed once.</summary>
    /// <exception cref="InvalidOperationException">The key is already completed.</exception>
    public void Complete(int statusCode, string? contentType, string? headers, string body, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (Status == IdempotencyStatus.Completed)
        {
            throw new InvalidOperationException("The idempotency key already has its answer; it is never overwritten (ADR 0006).");
        }

        if (headers is { Length: > ResponseHeadersMaxLength })
        {
            throw new ArgumentException($"The stored headers of an answer have at most {ResponseHeadersMaxLength} characters.", nameof(headers));
        }

        Status = IdempotencyStatus.Completed;
        StatusCode = statusCode;
        ResponseContentType = contentType is null ? null : Cut(contentType, ResponseContentTypeMaxLength);
        ResponseHeaders = headers;
        ResponseBody = body;
        CompletedAtUtc = nowUtc;
    }

    private static string Cut(string text, int maxLength) => text.Length <= maxLength ? text : text[..maxLength];
}
