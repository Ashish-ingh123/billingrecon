using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Common;

/// <summary>
/// The text form of a row version (BR-804, ADR 0007): base64, as a response carries it in the ETag header and in its
/// <c>version</c> field, and as the client sends it back in If-Match. A use case compares the version the client read
/// with the one it loaded in this form.
/// </summary>
public static class RowVersionText
{
    /// <summary>The version of <paramref name="entity"/> as last read or saved.</summary>
    public static string Of(IHasRowVersion entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return Convert.ToBase64String(entity.RowVersion);
    }
}
