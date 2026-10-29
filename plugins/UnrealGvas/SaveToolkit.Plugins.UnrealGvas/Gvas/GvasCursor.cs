using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Binary;

namespace SaveToolkit.Plugins.UnrealGvas.Gvas;

internal sealed class GvasCursor(ReadOnlyMemory<byte> data, int start = 0, int? end = null)
{
    public ReadOnlyMemory<byte> Data { get; } = data;
    public int Position { get; set; } = start;
    public int End { get; } = end ?? data.Length;
    public int Remaining => End - Position;

    public ReadOnlySpan<byte> Take(int length)
    {
        if (length < 0 || length > Remaining)
            throw new SaveFormatException($"Truncated GVAS data at 0x{Position:X} (need {length} bytes).");
        var result = Data.Span.Slice(Position, length);
        Position += length;
        return result;
    }

    public byte Byte() => Take(1)[0];
    public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

    public int Count(int maximum = GvasFile.MaxNodes)
    {
        var count = Int32();
        if (count < 0 || count > maximum)
            throw new SaveFormatException($"Invalid GVAS count {count} at 0x{Position - 4:X}.");
        return count;
    }

    public string String()
    {
        var length = Int32();
        var body = Take(FStringCodec.ByteLength(length, GvasBinary.Limits, GvasBinary.Context));
        return FStringCodec.Decode(length, body, GvasBinary.Limits, GvasBinary.Context);
    }
}
