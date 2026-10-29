using System.Buffers.Binary;

namespace SaveToolkit.Sdk.Binary;

/// <summary>A stack-only, zero-copy reader. Positions and subrange diagnostics use absolute source offsets.</summary>
public ref struct BoundedBinaryCursor
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public BoundedBinaryCursor(ReadOnlySpan<byte> data, string context, int start = 0, int? end = null)
    {
        var bound = end ?? data.Length;
        if (start < 0 || bound < start || bound > data.Length)
            throw new SaveFormatException($"{context}: invalid binary cursor bounds.");
        _data = data;
        Context = context;
        Start = start;
        End = bound;
        _position = start;
    }

    public string Context { get; }
    public int Start { get; }
    public int End { get; }
    public int Remaining => End - Position;
    public int Position
    {
        readonly get => _position;
        set
        {
            if (value < Start || value > End)
                throw new SaveFormatException($"{Context}: position 0x{value:X} exceeds the cursor bounds.");
            _position = value;
        }
    }

    public ReadOnlySpan<byte> Take(int length)
    {
        if (length < 0 || length > Remaining)
            throw new SaveFormatException($"{Context}: truncated data at 0x{Position:X} (need {length} bytes).");
        var result = _data.Slice(Position, length);
        _position += length;
        return result;
    }

    /// <summary>Consumes length bytes in the parent; the child cannot seek or read outside that range.</summary>
    public BoundedBinaryCursor Subrange(int length)
    {
        var start = Position;
        _ = Take(length);
        return new(_data, Context, start, Position);
    }

    public byte ReadByte() => Take(sizeof(byte))[0];
    public short ReadInt16LE() => BinaryPrimitives.ReadInt16LittleEndian(Take(sizeof(short)));
    public ushort ReadUInt16LE() => BinaryPrimitives.ReadUInt16LittleEndian(Take(sizeof(ushort)));
    public int ReadInt32LE() => BinaryPrimitives.ReadInt32LittleEndian(Take(sizeof(int)));
    public uint ReadUInt32LE() => BinaryPrimitives.ReadUInt32LittleEndian(Take(sizeof(uint)));
    public long ReadInt64LE() => BinaryPrimitives.ReadInt64LittleEndian(Take(sizeof(long)));
    public ulong ReadUInt64LE() => BinaryPrimitives.ReadUInt64LittleEndian(Take(sizeof(ulong)));
    public float ReadSingleLE() => BinaryPrimitives.ReadSingleLittleEndian(Take(sizeof(float)));
    public double ReadDoubleLE() => BinaryPrimitives.ReadDoubleLittleEndian(Take(sizeof(double)));

    public int ReadCount(int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        var count = ReadInt32LE();
        if (count < 0 || count > maximum)
            throw new SaveFormatException($"{Context}: invalid count {count} at 0x{Position - sizeof(int):X}.");
        return count;
    }
}
