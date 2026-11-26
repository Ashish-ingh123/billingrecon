using System.Buffers.Binary;

namespace BillingRecon.Domain.Identity;

/// <summary>
/// Reads what an uploaded image is from its own bytes (ADR 0059): the signature of PNG, JPEG or WebP at its start and
/// the width and height its header states. What the client called the file is never believed. The pixels are not
/// decoded: no image library is used, so a file whose header is sound and whose data is damaged is stored and shows as
/// a broken image.
/// </summary>
public static class LetterheadImageInspector
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The media type of <paramref name="format"/>.</summary>
    public static string ContentTypeOf(LetterheadImageFormat format) => format switch
    {
        LetterheadImageFormat.Png => "image/png",
        LetterheadImageFormat.Jpeg => "image/jpeg",
        LetterheadImageFormat.Webp => "image/webp",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The format is not one the enum defines."),
    };

    /// <summary>Whether <paramref name="content"/> is a PNG, JPEG or WebP image whose header states its size.</summary>
    public static bool TryRead(ReadOnlySpan<byte> content, out LetterheadImageInfo info)
        => TryReadPng(content, out info) || TryReadJpeg(content, out info) || TryReadWebp(content, out info);

    // PNG: the eight-byte signature, then the IHDR chunk, whose data starts with the width and the height.
    private static bool TryReadPng(ReadOnlySpan<byte> content, out LetterheadImageInfo info)
    {
        info = default;
        if (content.Length < 24 || !content.StartsWith(PngSignature) || !content.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(20, 4));

        return TryMake(LetterheadImageFormat.Png, width, height, out info);
    }

    // JPEG: FF D8, then segments; the first start-of-frame segment states the height and the width.
    private static bool TryReadJpeg(ReadOnlySpan<byte> content, out LetterheadImageInfo info)
    {
        info = default;
        if (content.Length < 4 || content[0] != 0xFF || content[1] != 0xD8)
        {
            return false;
        }

        var position = 2;
        while (position + 4 <= content.Length)
        {
            if (content[position] != 0xFF)
            {
                return false;
            }

            var marker = content[position + 1];
            if (marker == 0xFF)
            {
                // A fill byte before a marker.
                position++;
                continue;
            }

            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
            {
                // Markers with no length.
                position += 2;
                continue;
            }

            if (marker is 0xD9 or 0xDA)
            {
                // The end of the image, or its scan, before any frame.
                return false;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(position + 2, 2));
            if (length < 2)
            {
                return false;
            }

            var isStartOfFrame = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
            if (isStartOfFrame)
            {
                if (position + 9 > content.Length)
                {
                    return false;
                }

                var height = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(position + 5, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(position + 7, 2));

                return TryMake(LetterheadImageFormat.Jpeg, width, height, out info);
            }

            position += 2 + length;
        }

        return false;
    }

    // WebP: RIFF, a length, WEBP, then one of three first chunks, each stating the size its own way.
    private static bool TryReadWebp(ReadOnlySpan<byte> content, out LetterheadImageInfo info)
    {
        info = default;
        if (content.Length < 30 || !content[..4].SequenceEqual("RIFF"u8) || !content.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return false;
        }

        var chunk = content.Slice(12, 4);
        if (chunk.SequenceEqual("VP8X"u8))
        {
            var width = 1u + (uint)(content[24] | (content[25] << 8) | (content[26] << 16));
            var height = 1u + (uint)(content[27] | (content[28] << 8) | (content[29] << 16));

            return TryMake(LetterheadImageFormat.Webp, width, height, out info);
        }

        if (chunk.SequenceEqual("VP8 "u8))
        {
            // The frame tag (three bytes), the start code 9D 01 2A, then fourteen bits each of width and height.
            if (content[23] != 0x9D || content[24] != 0x01 || content[25] != 0x2A)
            {
                return false;
            }

            var width = (uint)(BinaryPrimitives.ReadUInt16LittleEndian(content.Slice(26, 2)) & 0x3FFF);
            var height = (uint)(BinaryPrimitives.ReadUInt16LittleEndian(content.Slice(28, 2)) & 0x3FFF);

            return TryMake(LetterheadImageFormat.Webp, width, height, out info);
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            // The signature byte 2F, then fourteen bits of width minus one and fourteen of height minus one.
            if (content[20] != 0x2F)
            {
                return false;
            }

            var bits = BinaryPrimitives.ReadUInt32LittleEndian(content.Slice(21, 4));
            var width = 1u + (bits & 0x3FFF);
            var height = 1u + ((bits >> 14) & 0x3FFF);

            return TryMake(LetterheadImageFormat.Webp, width, height, out info);
        }

        return false;
    }

    private static bool TryMake(LetterheadImageFormat format, uint width, uint height, out LetterheadImageInfo info)
    {
        info = default;
        if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
        {
            return false;
        }

        info = new LetterheadImageInfo(format, (int)width, (int)height);
        return true;
    }
}
