using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.UnrealGvas.Gvas;

/// <summary>Shared header validation for full parsing and bounded, property-free probing.</summary>
internal static class GvasHeaderReader
{
    private const int CustomVersionFormat = 3;
    private const int CustomVersionEntryBytes = 16 + sizeof(int); // GUID and version.

    public static GvasHeader Read(Stream stream)
    {
        try
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            Span<byte> magic = stackalloc byte[4];
            stream.ReadExactly(magic);
            if (!magic.SequenceEqual("GVAS"u8)) throw new SaveFormatException("Not a GVAS SaveGame file.");
            var version = reader.ReadInt32();
            if (version is < 1 or > 3) throw new SaveFormatException($"Unsupported GVAS save version {version}.");
            var package = reader.ReadInt32();
            int? package5 = version >= 3 ? reader.ReadInt32() : null;
            var major = reader.ReadUInt16();
            var minor = reader.ReadUInt16();
            var patch = reader.ReadUInt16();
            var build = reader.ReadUInt32();
            var branch = GvasBinary.ReadString(reader);
            if (major is < 4 or > 5) throw new SaveFormatException($"Unsupported Unreal engine version {major}.{minor}.");
            var count = 0;
            if (major >= 5 || minor >= 12)
            {
                var format = reader.ReadInt32();
                if (format != CustomVersionFormat)
                    throw new SaveFormatException($"Unsupported custom version format {format}; expected GUID format 3.");
                count = reader.ReadInt32();
                if (count < 0 || count > GvasFile.MaxNodes) throw new SaveFormatException($"Invalid GVAS count {count}.");
                SkipVersions(stream, count * CustomVersionEntryBytes);
            }
            var saveClass = GvasBinary.ReadString(reader);
            var header = new GvasHeader(version, package, package5, major, minor, patch, build, branch, count, saveClass);
            if (header.CompleteTypeNames && reader.ReadByte() != 0)
                throw new SaveFormatException("Unsupported UE5 root serialization control flags.");
            return header;
        }
        catch (EndOfStreamException ex) { throw new SaveFormatException("Truncated GVAS header.", ex); }
    }

    private static void SkipVersions(Stream stream, int length)
    {
        if (stream.CanSeek)
        {
            if (length > stream.Length - stream.Position) throw new SaveFormatException("Truncated GVAS custom versions.");
            stream.Seek(length, SeekOrigin.Current);
            return;
        }
        // Wrapped and non-seekable inputs are consumed in fixed-size chunks, never decoded as properties.
        Span<byte> buffer = stackalloc byte[1024];
        while (length > 0)
        {
            var chunk = Math.Min(length, buffer.Length);
            stream.ReadExactly(buffer[..chunk]);
            length -= chunk;
        }
    }
}
