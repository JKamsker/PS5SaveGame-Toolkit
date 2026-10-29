using System.Buffers.Binary;
using System.Security.Cryptography;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed record FactValue(short StoredValue, uint TimeBits, uint ExpiryBits);
internal sealed record SavedFact(string Name, ushort OpaqueWord, IReadOnlyList<FactValue> Entries)
{
    public byte[] NameBytes { get; init; } = [];
    public bool OpaqueName { get; init; }
}

/// <summary>Exact-consumption facts projection. Neither opaque words nor entry sums become quest state.</summary>
internal static class FactsReader
{
    private const int MaximumFacts = 100_000;
    private const int EntrySize = sizeof(short) + 2 * sizeof(uint);

    public static IReadOnlyList<SavedFact>? Read(Sav3Tree tree, byte[] data)
    {
        var roots = tree.Roots.Select(id => tree.Nodes[id]).Where(n => n.Tag == "BS" && n.Name == "facts").ToArray();
        if (roots.Length == 0) { return null; }
        if (roots.Length != 1 || roots[0].Children.Count != 1 || tree.Nodes[roots[0].Children[0]] is not { Tag: "SS" } stream)
        {
            throw new SaveFormatException("Ambiguous or unsupported facts archive.");
        }

        var raw = BinaryData.Slice(data, stream.Offset + 6, stream.Size - 6);
        if (raw.Length < 16 || !raw.Slice(4, 12).SequenceEqual(BinaryData.Slice(data, 4, 12)))
        {
            throw new SaveFormatException("Facts archive schema differs from the outer save.");
        }

        return ReadArchive(raw);
    }

    internal static IReadOnlyList<SavedFact> ReadArchive(ReadOnlySpan<byte> raw)
    {
        if (!BinaryData.Slice(raw, 0, 4).SequenceEqual("SXAP"u8) || !BinaryData.Slice(raw, 16, 4).SequenceEqual("SBDF"u8))
        {
            throw new SaveFormatException("Unsupported facts stream header.");
        }

        var count = BinaryData.Int32(raw, 20);
        if (count < 0 || count > MaximumFacts || count > (raw.Length - 28) / 5)
        {
            throw new SaveFormatException("Invalid declared facts count.");
        }

        var result = new List<SavedFact>(count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var position = 24;
        for (var i = 0; i < count; i++)
        {
            var encodedName = SavedString.ReadEncoded(raw, ref position, out var narrow);
            string? decodedName;
            try { decodedName = SavedString.Decode(encodedName, narrow); }
            catch (SaveFormatException) { decodedName = null; }
            // Samples contain bounded wide-name payloads with control characters and invalid surrogates.
            // Their meaning is unknown. Keep bytes and a hash identity instead of inventing quest names.
            var opaqueName = string.IsNullOrWhiteSpace(decodedName) || decodedName.Any(char.IsControl);
            var name = opaqueName ? $"opaque-name/{i}/{Convert.ToHexString(SHA256.HashData(encodedName))}" : decodedName!;
            if (!names.Add(name))
            {
                throw new SaveFormatException("Invalid or duplicate fact identifier.");
            }

            var opaque = BinaryData.UInt16(raw, position);
            var entryCount = BinaryData.UInt16(raw, position + 2);
            position += 4;
            var entries = ReadValues(raw, ref position, entryCount);
            result.Add(new SavedFact(name, opaque, entries) { NameBytes = encodedName.ToArray(), OpaqueName = opaqueName });
        }

        if (position != raw.Length - 4 || !raw[position..].SequenceEqual("EBDF"u8))
        {
            throw new SaveFormatException("Facts records do not consume the archive exactly through EBDF.");
        }

        return result;
    }

    private static FactValue[] ReadValues(ReadOnlySpan<byte> raw, ref int position, int entryCount)
    {
        var bytes = BinaryData.Slice(raw, position, entryCount * EntrySize);
        var entries = new FactValue[entryCount];
        for (var entry = 0; entry < entryCount; entry++)
        {
            var value = bytes.Slice(entry * EntrySize, EntrySize);
            entries[entry] = new FactValue(BinaryPrimitives.ReadInt16LittleEndian(value),
                BinaryPrimitives.ReadUInt32LittleEndian(value[2..]), BinaryPrimitives.ReadUInt32LittleEndian(value[6..]));
        }

        position += bytes.Length;
        return entries;
    }
}
