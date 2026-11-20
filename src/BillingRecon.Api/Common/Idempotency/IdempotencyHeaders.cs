using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Primitives;

namespace BillingRecon.Api.Common.Idempotency;

/// <summary>The two headers of the idempotency contract (BR-803, ADR 0006; docs/api.md, section 7).</summary>
public static class IdempotencyHeaders
{
    /// <summary>The request header that carries the key: one UUID per operation, sent again unchanged on a retry.</summary>
    public const string Key = "Idempotency-Key";

    /// <summary>The response header that marks a replay: the stored answer of an earlier request, not a new one.</summary>
    public const string Replayed = "Idempotent-Replayed";

    /// <summary>
    /// Whether <paramref name="values"/> is exactly one UUID that is not the empty one. Anything else (no header, two
    /// headers, a list, text that is no UUID) is not a key: the request is refused rather than run without one.
    /// </summary>
    public static bool TryParseKey(StringValues values, [NotNullWhen(true)] out Guid? key)
    {
        if (values.Count == 1 && Guid.TryParseExact(values[0]?.Trim(), "D", out var parsed) && parsed != Guid.Empty)
        {
            key = parsed;
            return true;
        }

        key = null;
        return false;
    }
}
