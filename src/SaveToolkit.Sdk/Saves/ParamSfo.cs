using System.Buffers.Binary;
using System.Text;

namespace SaveToolkit.Sdk.Saves;

/// <summary>Parsed <c>sce_sys/param.sfo</c>. Values are strings or 32-bit integers; binary blobs are hex.</summary>
public sealed class ParamSfo
{
    private const uint Magic = 0x46535000; // "\0PSF"
    private const ushort FormatUtf8Special = 0x0004;
    private const ushort FormatUtf8 = 0x0204;
    private const ushort FormatInt32 = 0x0404;

    public ParamSfo(IReadOnlyDictionary<string, object> entries) => Entries = entries;

    public IReadOnlyDictionary<string, object> Entries { get; }

    public string? MainTitle => GetString("MAINTITLE");
    public string? SubTitle => GetString("SUBTITLE");
    public string? Detail => GetString("DETAIL");
    public string? TitleId => GetString("TITLE_ID");
    public string? SaveDataDirectory => GetString("SAVEDATA_DIRECTORY");

    public string? GetString(string key) => Entries.TryGetValue(key, out var v) ? v as string : null;

    /// <summary>Parses param.sfo bytes. Returns null when the data is not a valid SFO.</summary>
    public static ParamSfo? TryParse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic)
        {
            return null;
        }

        var keyTable = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        var dataTable = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
        var count = BinaryPrimitives.ReadInt32LittleEndian(data[16..]);
        if (count < 0 || 20 + (count * 16) > data.Length || keyTable > data.Length || dataTable > data.Length)
        {
            return null;
        }

        var entries = new Dictionary<string, object>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var e = data.Slice(20 + (i * 16), 16);
            var keyOffset = BinaryPrimitives.ReadUInt16LittleEndian(e);
            var format = BinaryPrimitives.ReadUInt16LittleEndian(e[2..]);
            var length = BinaryPrimitives.ReadInt32LittleEndian(e[4..]);
            var valueOffset = BinaryPrimitives.ReadInt32LittleEndian(e[12..]);

            var keyStart = keyTable + keyOffset;
            var valueStart = dataTable + valueOffset;
            if (keyStart >= data.Length || valueStart < 0 || length < 0 || valueStart + length > data.Length)
            {
                return null;
            }

            var keyEnd = data[keyStart..].IndexOf((byte)0);
            var key = Encoding.ASCII.GetString(data.Slice(keyStart, keyEnd < 0 ? data.Length - keyStart : keyEnd));
            var raw = data.Slice(valueStart, length);
            entries[key] = format switch
            {
                FormatInt32 when length >= 4 => BinaryPrimitives.ReadInt32LittleEndian(raw),
                FormatUtf8 or FormatUtf8Special => DecodeText(raw),
                _ => Convert.ToHexString(raw.Length > 64 ? raw[..64] : raw),
            };
        }

        return new ParamSfo(entries);
    }

    private static string DecodeText(ReadOnlySpan<byte> raw)
    {
        var end = raw.IndexOf((byte)0);
        var text = end < 0 ? raw : raw[..end];
        try
        {
            return new UTF8Encoding(false, true).GetString(text);
        }
        catch (DecoderFallbackException)
        {
            // Some dumpers write Latin-1 (e.g. "®"); fall back instead of producing replacement chars.
            return Encoding.Latin1.GetString(text);
        }
    }
}
