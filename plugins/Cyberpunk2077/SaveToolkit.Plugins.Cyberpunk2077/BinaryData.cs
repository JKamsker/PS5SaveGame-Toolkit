using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal static class BinaryData
{
    public const int MaxExpandedSize = 64 * 1024 * 1024;
    public const int MaxFileSize = 128 * 1024 * 1024;

    public static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> data, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > data.Length - count)
        {
            throw new SaveFormatException($"CSAV range at 0x{offset:X} exceeds its containing data.");
        }

        return data.Slice(offset, count);
    }

    public static int Int32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(Slice(data, offset, sizeof(int)));

    public static uint UInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(Slice(data, offset, sizeof(uint)));

    public static ulong UInt64(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(Slice(data, offset, sizeof(ulong)));

    public static int PackedInt(ReadOnlySpan<byte> data, ref int position)
    {
        var first = Slice(data, position++, 1)[0];
        long magnitude = first & 0x3F;
        var continuation = (first & 0x40) != 0;
        var shift = 6;
        while (continuation)
        {
            if (shift > 27)
            {
                throw new SaveFormatException("CSAV packed integer is too long.");
            }

            var next = Slice(data, position++, 1)[0];
            magnitude |= (long)(next & 0x7F) << shift;
            continuation = (next & 0x80) != 0;
            shift += 7;
        }

        if (magnitude > int.MaxValue)
        {
            throw new SaveFormatException("CSAV packed integer exceeds the supported range.");
        }

        return (first & 0x80) != 0 ? -(int)magnitude : (int)magnitude;
    }

    public static string PackedName(ReadOnlySpan<byte> data, ref int position)
    {
        var length = PackedInt(data, ref position);
        const int maxNameLength = 1024;
        if (length > 0 || length < -maxNameLength)
        {
            throw new SaveFormatException("Unsupported CSAV node name encoding or length.");
        }

        var bytes = Slice(data, position, -length);
        if (bytes.ContainsAnyExceptInRange((byte)0x20, (byte)0x7E))
        {
            throw new SaveFormatException("CSAV node name is not printable ASCII.");
        }

        position -= length;
        return Encoding.ASCII.GetString(bytes);
    }
}
