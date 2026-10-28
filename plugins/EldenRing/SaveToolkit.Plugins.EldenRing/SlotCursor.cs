using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.EldenRing;

internal sealed record SaveRange(string Name, int Offset, int Length);

internal ref struct SlotCursor
{
    private readonly ReadOnlySpan<byte> _data;

    public SlotCursor(ReadOnlySpan<byte> data, int position = 0)
    {
        _data = data;
        Position = position;
    }

    public int Position { get; private set; }
    public int Remaining => _data.Length - Position;

    public uint UInt32()
    {
        var start = Take(sizeof(uint));
        return BinaryPrimitives.ReadUInt32LittleEndian(_data[start..]);
    }

    public int Take(int length)
    {
        if (length < 0 || Position < 0 || length > Remaining)
            throw new SaveFormatException("A semantic range extends beyond its character slot.");
        var start = Position;
        Position += length;
        return start;
    }

    public void SkipRecords(uint count, int size)
    {
        if (count > Remaining / size)
            throw new SaveFormatException("A variable record count extends beyond its character slot.");
        Take((int)count * size);
    }
}
