using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

internal static class RagnarokFormat
{
    public const int HeaderSize = 0x40;
    public const int KindOffset = 8;
    public const int RevisionOffset = 12;
    public const int PreferencesLengthOffset = 16;
    public const int LayoutWordOffset = 20;
    public const uint GameplayKind = 8;
    public const uint PreferencesKind = 31;
    public const uint MetadataKind = 35;
    public const uint LayoutWord = 0x008C0400;
    public const int PreferencesSize = 0x2B8;
    public const int MaximumFileSize = 32 * 1024 * 1024;
    public const uint Revision = 0x00513040;
    public const int MetadataOffset = HeaderSize;
    public const int MetadataSize = 0x400;
    public const int MetadataKindOffset = MetadataOffset + KindOffset;
    public const int MetadataRevisionOffset = MetadataOffset + RevisionOffset;
    public const int DescriptionOffset = 0x50;
    public const int DescriptionEnd = 0x190;
    public const int PlaySecondsOffset = 0x190;
    public const int SaveCounterOffset = 0x1BC;
    public const int DirectoryMarkerOffset = MetadataOffset + MetadataSize;
    public const ushort DirectoryMarker = 0x61C0;
    public const int DirectoryCountOffset = DirectoryMarkerOffset + sizeof(ushort);
    public const int DirectoryItemsOffset = DirectoryCountOffset + sizeof(uint);
    public const int MaximumRecordCount = 65_536;
    public const int WalletHeaderSize = 20;
    public const int WalletRowSize = 16;
    public const int WalletRowMetadataOffset = 12;
    public const ulong HeroWalletId = 0x00004895C643225F;

    public static uint UInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);

    public static ulong UInt64(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(bytes[offset..]);

    public static bool TryFileKind(string name, out bool preferences)
    {
        preferences = name.Equals("userpreferences", StringComparison.OrdinalIgnoreCase);
        if (preferences)
        {
            return true;
        }

        const string prefix = "GOWRSAVE";
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            name.Length > prefix.Length && name.AsSpan(prefix.Length).IndexOfAnyExceptInRange('0', '9') < 0 &&
            int.TryParse(name.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    public static bool MatchesHeader(ReadOnlySpan<byte> bytes, long length, bool preferences) =>
        bytes.Length >= HeaderSize && length <= MaximumFileSize &&
        (preferences ? length == PreferencesSize : length >= DirectoryItemsOffset) &&
        UInt32(bytes, KindOffset) == (preferences ? PreferencesKind : GameplayKind) && UInt32(bytes, RevisionOffset) == Revision &&
        (preferences || (UInt32(bytes, PreferencesLengthOffset) == PreferencesSize && UInt32(bytes, LayoutWordOffset) == LayoutWord));

    public static int CheckedCount(ReadOnlySpan<byte> bytes, int offset, int stride)
    {
        if (offset < 0 || offset > bytes.Length - sizeof(uint))
        {
            throw new SaveFormatException($"Truncated record count at 0x{offset:X}.");
        }

        var count = UInt32(bytes, offset);
        RequireRange(bytes, offset + sizeof(uint), count, stride);
        return (int)count;
    }

    public static void RequireRange(ReadOnlySpan<byte> bytes, int start, uint count, int stride)
    {
        if (count > MaximumRecordCount || start < 0 || start > bytes.Length || (long)count * stride > bytes.Length - start)
        {
            throw new SaveFormatException($"Invalid or truncated record array at 0x{start:X}.");
        }
    }

    public static string Description(ReadOnlySpan<byte> bytes)
    {
        var raw = bytes[DescriptionOffset..DescriptionEnd];
        var end = raw.IndexOf((byte)0);
        if (end < 0)
        {
            throw new SaveFormatException("The Ragnarök location description has no terminator.");
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(raw[..end]);
        }
        catch (DecoderFallbackException ex)
        {
            throw new SaveFormatException("The Ragnarök location description is not valid UTF-8.", ex);
        }
    }
}
