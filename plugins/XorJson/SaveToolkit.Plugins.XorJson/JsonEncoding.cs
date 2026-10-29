using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.XorJson;

internal sealed record JsonEncoding(bool Xor, bool Plain = false)
{
    internal string Label => Plain ? "JSON · FCharacterSaveV1" : Xor ? "JSON · XOR 1" : "JSON · byte minus one";

    internal byte[] Transform(ReadOnlySpan<byte> bytes, bool decode)
    {
        var result = new byte[bytes.Length];
        for (var i = 0; i < result.Length; i++)
            result[i] = TransformByte(bytes[i], decode);
        return result;
    }

    private byte TransformByte(byte value, bool decode) =>
        Plain ? value : Xor ? (byte)(value ^ 1) : unchecked((byte)(value + (decode ? 1 : -1)));

    internal static bool HasEncodedPrefix(ReadOnlySpan<byte> prefix) =>
        HasJsonPrefix(prefix, new JsonEncoding(true)) || HasJsonPrefix(prefix, new JsonEncoding(false));

    internal static DecodedJson Decode(byte[] bytes)
    {
        if (HasJsonPrefix(bytes))
        {
            var plainTree = JsonTokenTree.Parse(bytes);
            if (CharacterProfile.Read(plainTree) is not null)
                return new DecodedJson(bytes, plainTree, new JsonEncoding(false, true));
            throw new SaveFormatException("Plain JSON is not an FCharacterSaveV1 document.");
        }
        DecodedJson? match = null;
        foreach (var encoding in new[] { new JsonEncoding(true), new JsonEncoding(false) })
        {
            if (!HasJsonPrefix(bytes, encoding)) continue;
            var plain = encoding.Transform(bytes, true);
            JsonTokenTree tree;
            try { tree = JsonTokenTree.Parse(plain); }
            catch (SaveFormatException) { continue; }
            if (match is not null) throw new SaveFormatException("Ambiguous JSON encoding; refusing to guess.");
            match = new DecodedJson(plain, tree, encoding);
        }
        return match ?? throw new SaveFormatException("Not valid XOR-1 or byte-minus-one UTF-8 JSON.");
    }

    private static bool HasJsonPrefix(ReadOnlySpan<byte> bytes, JsonEncoding? encoding = null)
    {
        // Inspect candidate bytes in place, including a transformed UTF-8 BOM, before
        // allocating a full-file decode buffer. Plausible candidates still parse fully.
        byte Decode(byte value) => encoding?.TransformByte(value, true) ?? value;
        var start = bytes.Length >= 3 && Decode(bytes[0]) == 0xEF && Decode(bytes[1]) == 0xBB && Decode(bytes[2]) == 0xBF ? 3 : 0;
        var objectStarted = false;
        for (var i = start; i < bytes.Length; i++)
        {
            var value = Decode(bytes[i]);
            if (value is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t') continue;
            if (objectStarted) return value is (byte)'"' or (byte)'}';
            if (value == (byte)'[') return true;
            if (value != (byte)'{') return false;
            objectStarted = true;
        }
        // ReadHeader may stop before an object's first member. Full parsing, not
        // this inexpensive prefix check, establishes validity and ambiguity.
        return objectStarted;
    }
}

internal sealed record DecodedJson(byte[] Bytes, JsonTokenTree Tree, JsonEncoding Encoding);
