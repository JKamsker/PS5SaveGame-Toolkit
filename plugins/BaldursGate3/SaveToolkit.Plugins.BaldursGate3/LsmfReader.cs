using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>
/// Independently implemented from measured PS5 bytes and documented format facts.
/// No community parser implementation is incorporated; see LSMF.md for provenance.
/// Descriptor sizes describe nominal ranges, not a universal gameplay record layout.
/// </summary>
internal static class LsmfReader
{
    internal const int ArenaBase = 48;
    internal const int DescriptorSize = 48;
    internal const int MaxBlobBytes = 64 * 1024 * 1024;
    internal const int MaxComponents = 4096;
    internal const int MaxNameBytes = 512;
    internal const int MaxElementBytes = 1024 * 1024;
    internal const string EntityComponent = "core.v0.EntityId";
    private static ReadOnlySpan<byte> SupportedPrefix => [0x4c, 0x53, 0x4d, 0x46, 1, 1, 0, 8];

    internal static LsmfModel Read(ReadOnlyMemory<byte> bytes)
    {
        var data = bytes.Span;
        BinaryData.Require(data.Length is >= ArenaBase and <= MaxBlobBytes, "LSMF exceeds the supported size bounds.");
        BinaryData.Require(data.StartsWith(SupportedPrefix), "Unsupported LSMF signature/version; original bytes remain available raw.");
        var directory = Offset(BinaryData.U64(data, 16), data.Length - ArenaBase) + ArenaBase;
        BinaryData.Require(directory % sizeof(ulong) == 0, "Unaligned LSMF directory.");
        var directorySize = Offset(BinaryData.U64(data, 24), data.Length - directory);
        var descriptorRelative = Offset(BinaryData.U32(data, 32), directorySize);
        BinaryData.Require(descriptorRelative % sizeof(ulong) == 0, "Unaligned LSMF descriptor table.");
        var count = BinaryData.U16(data, 36);
        BinaryData.Require(count <= MaxComponents, "LSMF component budget exceeded.");
        BinaryData.Require((long)count * DescriptorSize <= directorySize - descriptorRelative,
            "LSMF descriptor table is truncated.");
        var descriptors = new List<LsmfComponent>(count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var descriptor = directory + descriptorRelative + index * DescriptorSize;
            var nameOffset = Offset(BinaryData.U64(data, descriptor), descriptorRelative);
            var nameLength = Offset(BinaryData.U64(data, descriptor + 8), MaxNameBytes);
            BinaryData.Require(nameLength > 0 && nameLength <= descriptorRelative - nameOffset,
                "Invalid LSMF component name range.");
            string name;
            try { name = BinaryData.Utf8.GetString(data.Slice(directory + nameOffset, nameLength)); }
            catch (DecoderFallbackException exception) { throw new SaveFormatException("Invalid LSMF UTF-8 name.", exception); }
            BinaryData.Require(!name.Any(char.IsControl) && names.Add(name), "Invalid or duplicate LSMF component name.");
            var element = Offset(BinaryData.U32(data, descriptor + 24), MaxElementBytes);
            var rows = Offset(BinaryData.U64(data, descriptor + 32), BinaryData.MaxRecords);
            BinaryData.Require(element > 0 || rows == 0, "LSMF nonempty component has zero element size.");
            var offset = Offset(BinaryData.U64(data, descriptor + 40), directory - ArenaBase) + ArenaBase;
            var length = (long)element * rows;
            BinaryData.Require(length <= directory - offset, "LSMF nominal component range leaves the arena.");
            descriptors.Add(new LsmfComponent(index, name, BinaryData.U64(data, descriptor + 16),
                BinaryData.U32(data, descriptor + 28), element, rows, offset, (int)length));
        }

        return new LsmfModel(bytes, directory, descriptors);
    }

    private static int Offset(ulong value, int maximum)
    {
        BinaryData.Require(maximum >= 0 && value <= (ulong)maximum, "LSMF offset/count is out of range or exceeds its budget.");
        return (int)value;
    }
}
