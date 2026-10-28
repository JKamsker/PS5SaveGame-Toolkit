using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.BaldursGate3;

internal static class BinaryData
{
    internal const int MaxBufferSize = 256 * 1024 * 1024;
    internal const int MaxRecords = 1_000_000;
    internal static readonly UTF8Encoding Utf8 = new(false, true);

    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new SaveFormatException(message);
        }
    }

    internal static int Size(ulong value)
    {
        Require(value <= MaxBufferSize, "BG3 buffer exceeds the supported 256 MiB limit.");
        return (int)value;
    }

    internal static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> data, int offset, int length)
    {
        Require(offset >= 0 && length >= 0 && offset <= data.Length && length <= data.Length - offset,
            "BG3 data is truncated or contains an invalid offset.");
        return data.Slice(offset, length);
    }

    internal static uint U32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(Slice(data, offset, sizeof(uint)));

    internal static int I32(ReadOnlySpan<byte> data, int offset) => unchecked((int)U32(data, offset));

    internal static ushort U16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(Slice(data, offset, sizeof(ushort)));

    internal static ulong U64(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(Slice(data, offset, sizeof(ulong)));
}
