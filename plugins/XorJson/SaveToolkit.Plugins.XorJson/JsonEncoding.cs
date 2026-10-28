using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.XorJson;

internal sealed record JsonEncoding(bool Xor)
{
    internal string Label => Xor ? "JSON · XOR 1" : "JSON · byte minus one";

    internal byte[] Transform(ReadOnlySpan<byte> bytes, bool decode)
    {
        var result = new byte[bytes.Length];
        for (var i = 0; i < result.Length; i++)
            result[i] = Xor ? (byte)(bytes[i] ^ 1) : unchecked((byte)(bytes[i] + (decode ? 1 : -1)));
        return result;
    }

    internal static bool HasEncodedPrefix(ReadOnlySpan<byte> prefix) =>
        HasJsonPrefix(new JsonEncoding(true).Transform(prefix, true)) ||
        HasJsonPrefix(new JsonEncoding(false).Transform(prefix, true));

    internal static DecodedJson Decode(byte[] bytes)
    {
        DecodedJson? match = null;
        foreach (var encoding in new[] { new JsonEncoding(true), new JsonEncoding(false) })
        {
            var plain = encoding.Transform(bytes, true);
            if (!HasJsonPrefix(plain)) continue;
            JsonTokenTree tree;
            try { tree = JsonTokenTree.Parse(plain); }
            catch (SaveFormatException) { continue; }
            if (match is not null) throw new SaveFormatException("Ambiguous JSON encoding; refusing to guess.");
            match = new DecodedJson(plain, tree, encoding);
        }
        return match ?? throw new SaveFormatException("Not valid XOR-1 or byte-minus-one UTF-8 JSON.");
    }

    private static bool HasJsonPrefix(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith("\uFEFF"u8)) bytes = bytes[3..];
        foreach (var value in bytes)
        {
            if (value is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t') continue;
            return value is (byte)'{' or (byte)'[';
        }
        return false;
    }
}

internal sealed record DecodedJson(byte[] Bytes, JsonTokenTree Tree, JsonEncoding Encoding);
