using System.Security.Cryptography;
using System.Text;

namespace BillingRecon.Api.Common.Idempotency;

/// <summary>
/// The fingerprint of a request, by which the idempotency store tells a retry from a different request that reuses
/// the key (ADR 0006): SHA-256 over the method, the path with its query string, the <c>If-Match</c> header and the
/// bytes of the body.
/// <para>
/// The query string and <c>If-Match</c> are part of it because they change what the request does. A client that is
/// answered 409 <c>concurrency_conflict</c>, reads the record again and sends its new version under the old key is
/// making a new attempt: it must be told so (422 <c>idempotency_key_reuse</c>) rather than be handed the old 409 again.
/// </para>
/// </summary>
public static class RequestFingerprint
{
    // Between the parts, so that no two different requests can be written as the same bytes: a line feed cannot
    // occur inside a method, a request target or a header value.
    private const byte Separator = (byte)'\n';

    /// <summary>The SHA-256 fingerprint, 32 bytes.</summary>
    /// <param name="method">The HTTP method; its case does not matter.</param>
    /// <param name="pathAndQuery">The path with the query string, as received.</param>
    /// <param name="ifMatch">The <c>If-Match</c> header as received; null or empty when there is none.</param>
    /// <param name="body">The bytes of the body, as received.</param>
    public static byte[] Compute(string method, string pathAndQuery, string? ifMatch, ReadOnlySpan<byte> body)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(pathAndQuery);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(method.ToUpperInvariant()));
        hash.AppendData([Separator]);
        hash.AppendData(Encoding.UTF8.GetBytes(pathAndQuery));
        hash.AppendData([Separator]);
        hash.AppendData(Encoding.UTF8.GetBytes(ifMatch ?? string.Empty));
        hash.AppendData([Separator]);
        hash.AppendData(body);

        return hash.GetHashAndReset();
    }
}
