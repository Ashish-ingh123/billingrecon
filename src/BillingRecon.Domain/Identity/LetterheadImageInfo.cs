namespace BillingRecon.Domain.Identity;

/// <summary>What the first bytes of an image say it is (ADR 0059): its format and its size in pixels.</summary>
public readonly record struct LetterheadImageInfo(LetterheadImageFormat Format, int Width, int Height)
{
    /// <summary>The media type of the format.</summary>
    public string ContentType => LetterheadImageInspector.ContentTypeOf(Format);
}
