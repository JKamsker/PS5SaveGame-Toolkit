using System.Security.Cryptography;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AtomicHeart;

internal sealed record SlotProperty(string Name, string Type, string? StructType, int Offset, int Length, FieldValue? Value);
internal sealed record CompressedChunk(int Offset, int CompressedBytes, int UncompressedBytes);

internal sealed record SaveExtensionFile(int PackageVersion, string Engine, int CustomVersions,
    IReadOnlyList<SlotProperty> Properties, int DataOffset, int DataLength, IReadOnlyList<CompressedChunk> Chunks)
{
    internal const int MaxBytes = 128 * 1024 * 1024;
    private const int DigestBytes = 20;
    private const int FileTypeTag = 1;
    private const int SaveVersion = 2;
    private const int GuidCustomVersionFormat = 3;
    private const int MaxCustomVersions = 4096;
    private const int MaxProperties = 1024;
    private const int ChunkSize = 128 * 1024;
    private const int MaxChunks = MaxBytes / ChunkSize;
    private const ulong ChunkMagic = 0x9E2A83C1;

    internal static SaveExtensionFile Parse(byte[] bytes)
    {
        if (bytes.Length is < DigestBytes or > MaxBytes) throw new SaveFormatException("Invalid SaveExtension file length.");
        // SHA-1 is the game's integrity format, not a security/authentication boundary.
#pragma warning disable CA5350 // Required by the existing Atomic Heart on-disk checksum format.
        if (!SHA1.HashData(bytes.AsSpan(0, bytes.Length - DigestBytes)).AsSpan().SequenceEqual(bytes.AsSpan(^DigestBytes)))
            throw new SaveFormatException("Atomic Heart SHA-1 trailer does not match the file.");
#pragma warning restore CA5350
        var reader = new SaveCursor(bytes, end: bytes.Length - DigestBytes);
        if (reader.Int32() != FileTypeTag || reader.Int32() != SaveVersion) throw new SaveFormatException("Unsupported SaveExtension tag/version.");
        var package = reader.Int32();
        var major = reader.UInt16();
        var minor = reader.UInt16();
        var patch = reader.UInt16();
        _ = reader.Int32();
        _ = reader.String();
        if (major != 4 || minor < 12 || reader.Int32() != GuidCustomVersionFormat) throw new SaveFormatException("Unsupported SaveExtension engine/custom versions.");
        var count = reader.Count(MaxCustomVersions);
        reader.Take(count * 20);
        if (reader.String() != "/Script/SaveExtension.SlotInfo") throw new SaveFormatException("Missing SaveExtension SlotInfo class.");
        var infoLength = reader.Count(reader.Remaining);
        var infoStart = reader.Position;
        reader.Take(infoLength);
        var properties = ReadProperties(bytes, infoStart, infoLength);
        if (reader.String() != "/Script/SaveExtension.SlotData") throw new SaveFormatException("Missing SaveExtension SlotData class.");
        // Atomic Heart replaces upstream's compression bool with an algorithm FString.
        if (reader.String() != "Oodle") throw new SaveFormatException("Unsupported Atomic Heart compression algorithm.");
        var dataLength = reader.Count(reader.Remaining);
        var dataStart = reader.Position;
        reader.Take(dataLength);
        if (reader.Remaining != 0) throw new SaveFormatException("Unexpected data before the Atomic Heart SHA-1 trailer.");
        var chunks = ReadChunks(bytes, dataStart, dataLength);
        return new SaveExtensionFile(package, $"{major}.{minor}.{patch}", count, properties, dataStart, dataLength, chunks);
    }

    private static IReadOnlyList<SlotProperty> ReadProperties(byte[] bytes, int start, int length)
    {
        var reader = new SaveCursor(bytes, start, start + length);
        var properties = new List<SlotProperty>();
        while (properties.Count < MaxProperties)
        {
            var name = reader.String();
            if (name == "None")
            {
                if (reader.Remaining != 4 || reader.Int32() != 0) throw new SaveFormatException("Invalid SlotInfo object terminator.");
                return properties;
            }
            var type = reader.String();
            var size = reader.Count(reader.Remaining);
            _ = reader.Int32(); // static-array index, preserved
            string? structType = null;
            if (type == "StructProperty") { structType = reader.String(); reader.Take(16); }
            else if (type is "ArrayProperty" or "SetProperty" or "ByteProperty" or "EnumProperty") _ = reader.String();
            else if (type == "MapProperty") { _ = reader.String(); _ = reader.String(); }
            if (type == "BoolProperty") _ = reader.Byte();
            var hasGuid = reader.Byte();
            if (hasGuid > 1) throw new SaveFormatException("Invalid SlotInfo property GUID flag.");
            if (hasGuid != 0) reader.Take(16);
            var offset = reader.Position;
            reader.Take(size);
            var value = new SaveCursor(bytes, offset, offset + size);
            FieldValue? decoded = type switch
            {
                "IntProperty" when size == 4 => FieldValue.Of(value.Int32()),
                "NameProperty" or "StrProperty" => FieldValue.Of(value.String()),
                "StructProperty" when size == 8 && structType is "Timespan" or "DateTime" => FieldValue.Of(value.Int64()),
                _ => null,
            };
            if (decoded is not null && value.Remaining != 0) throw new SaveFormatException("Invalid SlotInfo primitive payload size.");
            properties.Add(new SlotProperty(name, type, structType, offset, size, decoded));
        }
        throw new SaveFormatException("SlotInfo exceeds the 1,024 property safety limit.");
    }

    private static IReadOnlyList<CompressedChunk> ReadChunks(byte[] bytes, int start, int length)
    {
        var reader = new SaveCursor(bytes, start, start + length);
        var chunks = new List<CompressedChunk>();
        long total = 0;
        while (reader.Remaining > 0)
        {
            if (chunks.Count >= MaxChunks) throw new SaveFormatException("SlotData exceeds the 1,024 chunk safety limit.");
            var offset = reader.Position;
            if (reader.UInt64() != ChunkMagic || reader.UInt64() != ChunkSize) throw new SaveFormatException("Invalid Unreal compressed chunk header.");
            var compressed = reader.UInt64();
            var plain = reader.UInt64();
            if (compressed == 0 || compressed > (ulong)reader.Remaining || plain is 0 or > ChunkSize ||
                reader.UInt64() != compressed || reader.UInt64() != plain)
                throw new SaveFormatException("Invalid Unreal compressed chunk sizes.");
            var payload = reader.Take((int)compressed);
            // The shared codec validates Kraken, including block-level stored frames (CC 06).
            if (payload.Length < 2 || (payload[0] & 0x8F) != 0x8C || payload[1] != 0x06)
                throw new SaveFormatException("Unsupported Atomic Heart Oodle variant; only Kraken (decoder 6) is supported.");
            total += (long)plain;
            if (total > MaxBytes) throw new SaveFormatException("Declared SlotData exceeds the 128 MiB safety limit.");
            chunks.Add(new CompressedChunk(offset, (int)compressed, (int)plain));
        }
        if (chunks.Count == 0) throw new SaveFormatException("Empty Atomic Heart SlotData.");
        return chunks;
    }
}
