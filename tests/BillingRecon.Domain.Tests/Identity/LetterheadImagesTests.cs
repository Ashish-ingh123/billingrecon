using System.Buffers.Binary;
using System.Text;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Domain.Tests.Identity;

/// <summary>
/// The images of a letterhead in the domain (ADR 0059): what a file is, told from its own bytes; the stored image with
/// its hash; and the tenant naming its logo and signature by hash, with an event only for a change.
/// </summary>
public sealed class LetterheadImagesTests
{
    private static readonly DateTimeOffset Now = new(2027, 4, 2, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ADR0059_a_png_is_known_by_its_signature_and_its_header_states_its_size()
    {
        LetterheadImageInspector.TryRead(Png(320, 96), out var info).ShouldBeTrue();

        info.ShouldBe(new LetterheadImageInfo(LetterheadImageFormat.Png, 320, 96));
        info.ContentType.ShouldBe("image/png");
    }

    [Fact]
    public void ADR0059_a_jpeg_is_read_to_its_first_frame_past_the_segments_before_it()
    {
        LetterheadImageInspector.TryRead(Jpeg(640, 200), out var info).ShouldBeTrue();

        info.ShouldBe(new LetterheadImageInfo(LetterheadImageFormat.Jpeg, 640, 200));
        info.ContentType.ShouldBe("image/jpeg");
    }

    [Fact]
    public void ADR0059_a_webp_states_its_size_in_each_of_its_three_first_chunks()
    {
        LetterheadImageInspector.TryRead(WebpExtended(400, 120), out var extended).ShouldBeTrue();
        LetterheadImageInspector.TryRead(WebpLossy(400, 120), out var lossy).ShouldBeTrue();
        LetterheadImageInspector.TryRead(WebpLossless(400, 120), out var lossless).ShouldBeTrue();

        var expected = new LetterheadImageInfo(LetterheadImageFormat.Webp, 400, 120);
        (extended, lossy, lossless).ShouldBe((expected, expected, expected));
        expected.ContentType.ShouldBe("image/webp");
    }

    [Fact]
    public void ADR0059_an_svg_and_every_other_file_is_not_an_image_whatever_it_is_called()
    {
        var svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><script>alert(1)</script></svg>");
        var gif = Encoding.ASCII.GetBytes("GIF89a").Concat(new byte[40]).ToArray();
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7").Concat(new byte[40]).ToArray();

        LetterheadImageInspector.TryRead(svg, out _).ShouldBeFalse();
        LetterheadImageInspector.TryRead(gif, out _).ShouldBeFalse();
        LetterheadImageInspector.TryRead(pdf, out _).ShouldBeFalse();
        LetterheadImageInspector.TryRead([], out _).ShouldBeFalse();
    }

    [Fact]
    public void ADR0059_a_file_cut_short_or_with_no_size_is_refused_and_never_read_past_its_end()
    {
        var png = Png(320, 96);
        var jpeg = Jpeg(640, 200);

        // Every beginning of a sound file that stops before its size is stated.
        for (var length = 0; length < 24; length++)
        {
            LetterheadImageInspector.TryRead(png.AsSpan(0, length), out _).ShouldBeFalse();
        }

        // The frame of this JPEG starts at byte 27 and states its size in its first nine bytes.
        const int sizeStatedAt = 36;
        for (var length = 0; length < sizeStatedAt; length++)
        {
            LetterheadImageInspector.TryRead(jpeg.AsSpan(0, length), out _).ShouldBeFalse();
        }

        LetterheadImageInspector.TryRead(jpeg.AsSpan(0, sizeStatedAt), out _).ShouldBeTrue();

        LetterheadImageInspector.TryRead(Png(0, 96), out _).ShouldBeFalse("a width of zero is no image");
        LetterheadImageInspector.TryRead(WebpExtended(400, 120).AsSpan(0, 29), out _).ShouldBeFalse();

        // A JPEG whose segment claims more bytes than the file has.
        byte[] lying = [0xFF, 0xD8, 0xFF, 0xE0, 0xFF, 0xFF, 0x00, 0x00];
        LetterheadImageInspector.TryRead(lying, out _).ShouldBeFalse();
    }

    [Fact]
    public void ADR0059_a_stored_image_keeps_its_bytes_and_their_sha256()
    {
        var content = Png(320, 96);

        var image = TenantImage.Store(LetterheadImageKind.Logo, content, Now);

        image.Content.ShouldBe(content);
        image.Sha256.ShouldBe(TenantImage.HashOf(content));
        image.Sha256.Length.ShouldBe(TenantImage.Sha256Length);
        image.Sha256.ShouldBe(image.Sha256.ToLowerInvariant());
        (image.Kind, image.Format, image.Width, image.Height, image.StoredAtUtc).ShouldBe((LetterheadImageKind.Logo, LetterheadImageFormat.Png, 320, 96, Now));
    }

    [Fact]
    public void ADR0059_an_image_that_is_too_large_too_wide_or_no_image_is_never_stored()
    {
        Should.Throw<ArgumentException>(() => TenantImage.Store(LetterheadImageKind.Logo, Png(320, 96, TenantImage.LogoMaxBytes + 1), Now));
        Should.Throw<ArgumentException>(() => TenantImage.Store(LetterheadImageKind.Signature, Png(320, 96, TenantImage.SignatureMaxBytes + 1), Now));
        Should.Throw<ArgumentException>(() => TenantImage.Store(LetterheadImageKind.Logo, Png(TenantImage.MaxSidePixels + 1, 96), Now));
        Should.Throw<ArgumentException>(() => TenantImage.Store(LetterheadImageKind.Logo, Png(320, TenantImage.MaxSidePixels + 1), Now));
        Should.Throw<ArgumentException>(() => TenantImage.Store(LetterheadImageKind.Logo, Encoding.UTF8.GetBytes("<svg/>"), Now));
        Should.Throw<ArgumentException>(() => TenantImage.Store(LetterheadImageKind.Logo, [], Now));

        // The largest that is allowed is stored.
        TenantImage.Store(LetterheadImageKind.Logo, Png(TenantImage.MaxSidePixels, TenantImage.MaxSidePixels, TenantImage.LogoMaxBytes), Now).Content.Length.ShouldBe(TenantImage.LogoMaxBytes);
        TenantImage.Store(LetterheadImageKind.Signature, Png(320, 96, TenantImage.SignatureMaxBytes), Now).Content.Length.ShouldBe(TenantImage.SignatureMaxBytes);
    }

    [Fact]
    public void ADR0059_the_tenant_names_its_images_by_hash_and_only_a_change_is_an_event()
    {
        var tenant = Tenant.Create("northfield", "Northfield Care Hospital", Now).Value;
        tenant.ClearDomainEvents();
        var first = TenantImage.HashOf(Png(320, 96));
        var second = TenantImage.HashOf(Png(321, 96));

        (tenant.LogoSha256, tenant.SignatureSha256).ShouldBe((null, null));
        tenant.ChangeLetterheadImage(LetterheadImageKind.Logo, null).ShouldBeFalse("there is no logo to remove");

        tenant.ChangeLetterheadImage(LetterheadImageKind.Logo, first).ShouldBeTrue();
        tenant.ChangeLetterheadImage(LetterheadImageKind.Logo, first).ShouldBeFalse("the same image again is no change");
        tenant.ChangeLetterheadImage(LetterheadImageKind.Logo, second).ShouldBeTrue();
        tenant.ChangeLetterheadImage(LetterheadImageKind.Signature, first).ShouldBeTrue();
        tenant.ChangeLetterheadImage(LetterheadImageKind.Logo, null).ShouldBeTrue();

        (tenant.LogoSha256, tenant.SignatureSha256).ShouldBe((null, first));
        tenant.ImageSha256Of(LetterheadImageKind.Signature).ShouldBe(first);
        tenant.DomainEvents.ShouldBe(
        [
            new TenantLetterheadImageChanged(LetterheadImageKind.Logo, null, first),
            new TenantLetterheadImageChanged(LetterheadImageKind.Logo, first, second),
            new TenantLetterheadImageChanged(LetterheadImageKind.Signature, null, first),
            new TenantLetterheadImageChanged(LetterheadImageKind.Logo, second, null),
        ]);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")]
    [InlineData("g3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    public void ADR0059_a_hash_of_the_wrong_shape_is_a_bug_of_the_caller(string sha256)
    {
        var tenant = Tenant.Create("northfield", "Northfield Care Hospital", Now).Value;

        Should.Throw<ArgumentException>(() => tenant.ChangeLetterheadImage(LetterheadImageKind.Logo, sha256));
    }

    [Fact]
    public void ADR0059_the_signatory_has_a_designation_set_with_the_letterhead()
    {
        var tenant = Tenant.Create("northfield", "Northfield Care Hospital", Now).Value;

        tenant.ChangeLetterhead(new TenantLetterhead { Signatory = "Dr A. Example", SignatoryDesignation = "  Medical Superintendent " }).ShouldBeTrue();

        (tenant.Signatory, tenant.SignatoryDesignation).ShouldBe(("Dr A. Example", "Medical Superintendent"));
        tenant.Letterhead.SignatoryDesignation.ShouldBe("Medical Superintendent");
        tenant.ChangeLetterhead(new TenantLetterhead { Signatory = "Dr A. Example", SignatoryDesignation = "Medical Superintendent" }).ShouldBeFalse();
    }

    // The smallest beginnings of the three formats that state a size; the rest of a file is padding the inspector never reads.
    private static byte[] Png(uint width, uint height, int length = 64)
    {
        var bytes = new byte[length];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 }.CopyTo(bytes, 0);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    private static byte[] Jpeg(ushort width, ushort height)
    {
        var bytes = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        bytes.AddRange(new byte[14]);
        bytes.AddRange([0xFF, 0xDB, 0x00, 0x05, 0x00, 0x01, 0x02]);
        bytes.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03]);
        bytes.AddRange(new byte[12]);
        return [.. bytes];
    }

    private static byte[] Webp(ReadOnlySpan<byte> chunk)
    {
        var bytes = new byte[40];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 32);
        "WEBP"u8.CopyTo(bytes.AsSpan(8));
        chunk.CopyTo(bytes.AsSpan(12));
        return bytes;
    }

    private static byte[] WebpExtended(int width, int height)
    {
        var bytes = Webp("VP8X"u8);
        (bytes[24], bytes[25], bytes[26]) = ((byte)(width - 1), (byte)((width - 1) >> 8), (byte)((width - 1) >> 16));
        (bytes[27], bytes[28], bytes[29]) = ((byte)(height - 1), (byte)((height - 1) >> 8), (byte)((height - 1) >> 16));
        return bytes;
    }

    private static byte[] WebpLossy(ushort width, ushort height)
    {
        var bytes = Webp("VP8 "u8);
        (bytes[23], bytes[24], bytes[25]) = (0x9D, 0x01, 0x2A);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26), width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), height);
        return bytes;
    }

    private static byte[] WebpLossless(uint width, uint height)
    {
        var bytes = Webp("VP8L"u8);
        bytes[20] = 0x2F;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(21), (width - 1) | ((height - 1) << 14));
        return bytes;
    }
}
