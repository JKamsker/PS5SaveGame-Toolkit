using System.Text;

namespace SaveToolkit.Core.Plugins;

/// <summary>Best-effort identification of common container formats from a file header.</summary>
public static class FormatSniffer
{
    private static readonly (byte[] Magic, int Offset, string Name)[] Signatures =
    [
        ("GVAS"u8.ToArray(), 0, "Unreal Engine GVAS save"),
        ("DSSS"u8.ToArray(), 0, "RE Engine DSSS save (Capcom)"),
        ("LSPK"u8.ToArray(), 0, "Larian LSPK package"),
        ("LSOF"u8.ToArray(), 0, "Larian LSF binary resource"),
        ([0x89, 0x50, 0x4E, 0x47], 0, "PNG image"),
        ("RIFF"u8.ToArray(), 0, "RIFF container (WebP/WAV)"),
        ([0x1F, 0x8B], 0, "gzip stream"),
        ([0x28, 0xB5, 0x2F, 0xFD], 0, "Zstandard frame"),
        ([0x04, 0x22, 0x4D, 0x18], 0, "LZ4 frame"),
        ("PK"u8.ToArray(), 0, "ZIP archive"),
        ("SQLite format 3"u8.ToArray(), 0, "SQLite database"),
        ("\0PSF"u8.ToArray(), 0, "PlayStation param.sfo"),
        ([0x78, 0x9C], 0, "zlib stream"),
        ([0x78, 0xDA], 0, "zlib stream"),
        ([0x78, 0x01], 0, "zlib stream"),
    ];

    public static string? Identify(ReadOnlySpan<byte> header)
    {
        foreach (var (magic, offset, name) in Signatures)
        {
            if (header.Length >= offset + magic.Length && header.Slice(offset, magic.Length).SequenceEqual(magic))
            {
                return name;
            }
        }

        var trimmed = header.TrimStart(" \t\r\n"u8);
        if (trimmed.Length > 0 && (trimmed[0] == (byte)'{' || trimmed[0] == (byte)'[') && LooksLikeText(trimmed))
        {
            return "JSON text";
        }

        if (trimmed.StartsWith("<?xml"u8) || trimmed.StartsWith("<save"u8))
        {
            return "XML text";
        }

        return LooksLikeText(header) && header.Length > 0 ? "Text" : null;
    }

    /// <summary>Shannon entropy in bits per byte (0..8). Values near 8 suggest encryption or compression.</summary>
    public static double Entropy(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return 0;
        }

        Span<int> counts = stackalloc int[256];
        foreach (var b in data)
        {
            counts[b]++;
        }

        double entropy = 0;
        foreach (var c in counts)
        {
            if (c == 0)
            {
                continue;
            }

            var p = (double)c / data.Length;
            entropy -= p * Math.Log2(p);
        }

        return entropy;
    }

    private static bool LooksLikeText(ReadOnlySpan<byte> data)
    {
        var sample = data[..Math.Min(data.Length, 512)];
        try
        {
            _ = new UTF8Encoding(false, true).GetCharCount(sample[..TrimIncompleteUtf8(sample)]);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        foreach (var b in sample)
        {
            if (b < 0x09 || (b > 0x0D && b < 0x20))
            {
                return false;
            }
        }

        return true;
    }

    private static int TrimIncompleteUtf8(ReadOnlySpan<byte> s)
    {
        var end = s.Length;
        for (var i = 1; i <= 3 && end - i >= 0; i++)
        {
            var b = s[end - i];
            if ((b & 0xC0) == 0xC0)
            {
                return end - i;
            }

            if ((b & 0x80) == 0)
            {
                break;
            }
        }

        return end;
    }
}
