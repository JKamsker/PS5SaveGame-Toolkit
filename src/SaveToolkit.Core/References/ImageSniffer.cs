using System.Buffers.Binary;

namespace SaveToolkit.Core.References;

internal sealed record SniffedImage(string ContentType, int Width, int Height);

/// <summary>
/// Identifies safe raster images by their magic bytes (never by extension) and reads their dimensions.
/// Only PNG, JPEG and WebP pass; SVG, HTML and anything else is rejected because the bytes are served
/// to the browser.
/// </summary>
internal static class ImageSniffer
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static SniffedImage? Sniff(ReadOnlySpan<byte> data) =>
        Png(data) ?? Jpeg(data) ?? WebP(data);

    /// <summary>Extensions allowed for each content type.</summary>
    public static bool ExtensionMatches(string path, string contentType)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return contentType switch
        {
            "image/png" => ext == ".png",
            "image/jpeg" => ext is ".jpg" or ".jpeg",
            "image/webp" => ext == ".webp",
            _ => false,
        };
    }

    private static SniffedImage? Png(ReadOnlySpan<byte> d)
    {
        if (d.Length < 24 || !d.StartsWith(PngSignature) || !d.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return null;
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(d[16..]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(d[20..]);
        return width is 0 or > int.MaxValue || height is 0 or > int.MaxValue ? null : new("image/png", (int)width, (int)height);
    }

    private static SniffedImage? Jpeg(ReadOnlySpan<byte> d)
    {
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8)
        {
            return null;
        }

        var pos = 2;
        while (pos + 4 <= d.Length)
        {
            if (d[pos] != 0xFF)
            {
                return null;
            }

            var marker = d[pos + 1];
            if (marker == 0xFF)
            {
                pos++;
                continue;
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                pos += 2;
                continue;
            }

            if (marker is 0xD9 or 0xDA)
            {
                return null;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(d[(pos + 2)..]);
            if (length < 2)
            {
                return null;
            }

            if (IsStartOfFrame(marker))
            {
                if (pos + 9 > d.Length)
                {
                    return null;
                }

                var height = BinaryPrimitives.ReadUInt16BigEndian(d[(pos + 5)..]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(d[(pos + 7)..]);
                return width == 0 || height == 0 ? null : new("image/jpeg", width, height);
            }

            pos += 2 + length;
        }

        return null;
    }

    private static bool IsStartOfFrame(byte marker) => marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;

    private static SniffedImage? WebP(ReadOnlySpan<byte> d)
    {
        if (d.Length < 30 || !d.StartsWith("RIFF"u8) || !d.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return null;
        }

        var chunk = d.Slice(12, 4);
        if (chunk.SequenceEqual("VP8X"u8))
        {
            var w = 1 + (d[24] | (d[25] << 8) | (d[26] << 16));
            var h = 1 + (d[27] | (d[28] << 8) | (d[29] << 16));
            return new("image/webp", w, h);
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (d[20] != 0x2F)
            {
                return null;
            }

            var bits = BinaryPrimitives.ReadUInt32LittleEndian(d[21..]);
            return new("image/webp", (int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
        }

        if (chunk.SequenceEqual("VP8 "u8))
        {
            if (d[23] != 0x9D || d[24] != 0x01 || d[25] != 0x2A)
            {
                return null;
            }

            var w = BinaryPrimitives.ReadUInt16LittleEndian(d[26..]) & 0x3FFF;
            var h = BinaryPrimitives.ReadUInt16LittleEndian(d[28..]) & 0x3FFF;
            return w == 0 || h == 0 ? null : new("image/webp", w, h);
        }

        return null;
    }
}
