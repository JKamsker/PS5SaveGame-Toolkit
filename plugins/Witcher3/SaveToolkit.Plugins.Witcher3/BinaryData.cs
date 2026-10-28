using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

internal static class BinaryData
{
    public const int MaxExpandedSize = 64 * 1024 * 1024;
    public const int MaxFileSize = 128 * 1024 * 1024;

    public static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> data, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > data.Length - count)
        {
            throw new SaveFormatException($"Witcher save range at 0x{offset:X} exceeds its containing data.");
        }

        return data.Slice(offset, count);
    }

    public static int Int32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(Slice(data, offset, sizeof(int)));

    public static ushort UInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(Slice(data, offset, sizeof(ushort)));

    public static string Ascii(ReadOnlySpan<byte> data)
    {
        if (data.ContainsAnyExceptInRange((byte)0x20, (byte)0x7E))
        {
            throw new SaveFormatException("Witcher save name is not printable ASCII.");
        }

        return Encoding.ASCII.GetString(data);
    }

    public static int PayloadOffset(ReadOnlySpan<byte> data, int position, int headerSize)
    {
        var absolute = Int32(data, position);
        if (absolute < headerSize || absolute - headerSize > data.Length)
        {
            throw new SaveFormatException("Invalid SAV3 absolute offset.");
        }

        return absolute - headerSize;
    }
}
