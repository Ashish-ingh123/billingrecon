using System.Diagnostics.CodeAnalysis;

namespace BillingRecon.Api.Common;

/// <summary>
/// The version of a record over HTTP (BR-804, ADR 0007). A response carries it as a strong ETag: the row version in
/// base64, in quotes, the same value as the <c>version</c> field of the body. A request that changes the record sends
/// it back in If-Match. Weak tags, <c>*</c> and lists are not accepted: a change applies to the version the client read
/// and to no other.
/// </summary>
public static class EntityTags
{
    // A SQL Server rowversion is eight bytes.
    private const int RowVersionLength = 8;

    /// <summary>The ETag of <paramref name="version"/>, the base64 text of a row version.</summary>
    public static string Format(string version) => $"\"{version}\"";

    /// <summary>
    /// Reads one strong ETag as this API issues them. The version comes back in canonical base64, so it compares equal
    /// to the text of the row version however the client wrote the base64.
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out string? version)
    {
        version = null;
        if (value is null || value.Length < 2 || value[0] != '"' || value[^1] != '"')
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[RowVersionLength];
        if (!Convert.TryFromBase64String(value[1..^1], bytes, out var written) || written != RowVersionLength)
        {
            return false;
        }

        version = Convert.ToBase64String(bytes);
        return true;
    }
}
