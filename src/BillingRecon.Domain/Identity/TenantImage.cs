using System.Security.Cryptography;
using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>
/// An image of a hospital's letterhead as it was uploaded (ADR 0059): its bytes, what they are, and their SHA-256. A
/// row is written once and never changed or deleted. Which image the hospital prints now is the hash its tenant row
/// names; replacing an image stores a new row and removing one clears the hash, so the hash in an audit record always
/// finds the bytes it stood for. The row raises no event: the tenant's change is what is audited.
/// </summary>
public sealed class TenantImage : Entity<TenantImageId>, ITenantScoped, IAppendOnly
{
    /// <summary>The most bytes a logo may have.</summary>
    public const int LogoMaxBytes = 256 * 1024;

    /// <summary>The most bytes a signature may have.</summary>
    public const int SignatureMaxBytes = 128 * 1024;

    /// <summary>The most pixels a side of an image may have.</summary>
    public const int MaxSidePixels = 2000;

    /// <summary>The length of a SHA-256 in lower-case hexadecimal.</summary>
    public const int Sha256Length = 64;

    // Entity Framework creates an image with this constructor and then sets the properties.
    private TenantImage()
    {
        Content = [];
        Sha256 = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Which image of the letterhead this is.</summary>
    public LetterheadImageKind Kind { get; private set; }

    /// <summary>The format its bytes state.</summary>
    public LetterheadImageFormat Format { get; private set; }

    /// <summary>The file, byte for byte as it was uploaded.</summary>
    public byte[] Content { get; private set; }

    /// <summary>The SHA-256 of <see cref="Content"/>, in lower-case hexadecimal.</summary>
    public string Sha256 { get; private set; }

    /// <summary>The width in pixels its header states.</summary>
    public int Width { get; private set; }

    /// <summary>The height in pixels its header states.</summary>
    public int Height { get; private set; }

    /// <summary>When it was uploaded.</summary>
    public DateTimeOffset StoredAtUtc { get; private set; }

    /// <summary>The most bytes an image of <paramref name="kind"/> may have.</summary>
    public static int MaxBytesOf(LetterheadImageKind kind) => kind switch
    {
        LetterheadImageKind.Logo => LogoMaxBytes,
        LetterheadImageKind.Signature => SignatureMaxBytes,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The kind is not one the enum defines."),
    };

    /// <summary>The SHA-256 of <paramref name="content"/>, in lower-case hexadecimal.</summary>
    public static string HashOf(ReadOnlySpan<byte> content) => Convert.ToHexStringLower(SHA256.HashData(content));

    /// <summary>The image of <paramref name="kind"/> uploaded at <paramref name="storedAtUtc"/>.</summary>
    /// <exception cref="ArgumentException">The bytes are not a PNG, JPEG or WebP image, are too many, or state a side that is too long: bugs in the caller, whose validator refuses them first.</exception>
    public static TenantImage Store(LetterheadImageKind kind, byte[] content, DateTimeOffset storedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.Length == 0 || content.Length > MaxBytesOf(kind))
        {
            throw new ArgumentException($"An image of kind {kind} has between 1 and {MaxBytesOf(kind)} bytes.", nameof(content));
        }

        if (!LetterheadImageInspector.TryRead(content, out var info))
        {
            throw new ArgumentException("The bytes are not a PNG, JPEG or WebP image.", nameof(content));
        }

        if (info.Width > MaxSidePixels || info.Height > MaxSidePixels)
        {
            throw new ArgumentException($"An image has at most {MaxSidePixels} pixels a side.", nameof(content));
        }

        return new TenantImage
        {
            Kind = kind,
            Format = info.Format,
            Content = content,
            Sha256 = HashOf(content),
            Width = info.Width,
            Height = info.Height,
            StoredAtUtc = storedAtUtc,
        };
    }
}
