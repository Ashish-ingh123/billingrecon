namespace BillingRecon.Application.Common;

/// <summary>
/// Remembers the idempotency keys of the current tenant and the answers to their requests (BR-803, ADR 0006), and
/// holds the transaction that makes an answer and the work it reports permanent together (BR-007).
/// <para>
/// The life of a key, driven by the API's filter for financial POSTs:
/// </para>
/// <list type="number">
/// <item><see cref="ReserveAsync"/>, in a short transaction of its own, before anything else. When the key is free it
/// is reserved and the business transaction of the request is opened and held: from then on the unit of work of the
/// request saves into it without committing.</item>
/// <item>The use case runs.</item>
/// <item>Exactly one of: <see cref="CompleteAsync"/> for a success, which stores the answer inside the business
/// transaction and commits both; <see cref="RejectAsync"/> for a refusal, which rolls the business transaction back
/// (a number it took is free again) and then stores the answer; <see cref="ReleaseAsync"/> for a fault, which rolls
/// back and forgets the key, so that the client may try again.</item>
/// </list>
/// <para>
/// The tenant is the tenant of the current scope and is never passed in: two tenants may use the same key.
/// </para>
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Reserves <paramref name="key"/> for a request with the fingerprint <paramref name="requestHash"/>, or says why
    /// it cannot: the same request was answered before (the stored answer comes back), is still running, or the key
    /// belongs to another request.
    /// </summary>
    /// <param name="key">The key the client sent; not the empty UUID.</param>
    /// <param name="method">The HTTP method, for the record.</param>
    /// <param name="path">The path, for the record.</param>
    /// <param name="requestHash">The SHA-256 fingerprint of the request.</param>
    /// <param name="ct">Cancels the reservation.</param>
    public Task<IdempotencyReservation> ReserveAsync(Guid key, string method, string path, byte[] requestHash, CancellationToken ct);

    /// <summary>
    /// The request succeeded: stores <paramref name="response"/> under the key inside the business transaction and
    /// commits it. If this fails, nothing of the request is permanent; call <see cref="ReleaseAsync"/>.
    /// </summary>
    public Task CompleteAsync(Guid key, StoredResponse response, CancellationToken ct);

    /// <summary>
    /// The request was refused (a 4xx): rolls the business transaction back, then stores <paramref name="response"/>
    /// under the key, so that a retry gets the same refusal.
    /// </summary>
    public Task RejectAsync(Guid key, StoredResponse response, CancellationToken ct);

    /// <summary>The request failed with a fault or was abandoned: rolls the business transaction back and forgets the key.</summary>
    public Task ReleaseAsync(Guid key, CancellationToken ct);
}
