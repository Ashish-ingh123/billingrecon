using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>
/// Reads the logo or the signature of the hospital's letterhead (ADR 0059), for anyone who is signed in, as the
/// letterhead itself is read: whoever may print a document needs them. A hospital that has none is answered 404, and
/// so the image of another hospital can never be asked for: the tenant of the request decides whose it is.
/// </summary>
public sealed class GetLetterheadImageHandler(ITenantRepository tenants, ITenantImageRepository images)
    : IQueryHandler<GetLetterheadImageQuery, LetterheadImageDto>
{
    /// <inheritdoc />
    public async Task<Result<LetterheadImageDto>> HandleAsync(GetLetterheadImageQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!Enum.IsDefined(query.Kind))
        {
            return IdentityErrors.LetterheadImageNotFound();
        }

        var tenant = await tenants.GetCurrentAsync(ct);
        if (tenant.ImageSha256Of(query.Kind) is not { } sha256)
        {
            return IdentityErrors.LetterheadImageNotFound();
        }

        var image = await images.FindAsync(query.Kind, sha256, ct);

        return image is null
            ? IdentityErrors.LetterheadImageNotFound()
            : new LetterheadImageDto(LetterheadImageInspector.ContentTypeOf(image.Format), image.Content, image.Sha256);
    }
}
