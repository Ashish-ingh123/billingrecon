namespace BillingRecon.Domain.Identity;

/// <summary>The formats an image of the letterhead may have (ADR 0059). SVG is not one: it is a document that can carry script.</summary>
public enum LetterheadImageFormat
{
    /// <summary>PNG, <c>image/png</c>.</summary>
    Png = 1,

    /// <summary>JPEG, <c>image/jpeg</c>.</summary>
    Jpeg = 2,

    /// <summary>WebP, <c>image/webp</c>.</summary>
    Webp = 3,
}
