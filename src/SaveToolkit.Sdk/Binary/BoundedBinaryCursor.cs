using System.Buffers.Binary;

namespace SaveToolkit.Sdk.Binary;

/// <summary>A stack-only, zero-copy reader. Positions and subrange diagnostics use absolute source offsets.</summary>
public ref struct BoundedBinaryCursor
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    /// <summary>Starts at an inclusive source offset with an exclusive end, defaulting to the complete span.</summary>
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

    /// <summary>Caller-supplied format context included in parse failures.</summary>
    public string Context { get; }
    /// <summary>Inclusive absolute lower bound.</summary>
    public int Start { get; }
    /// <summary>Exclusive absolute upper bound.</summary>
    public int End { get; }
    /// <summary>Unread bytes inside this cursor's bounds.</summary>
    public int Remaining => End - Position;
    /// <summary>Absolute read position; assigning outside the bounds throws a format exception.</summary>
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

    /// <summary>Consumes exactly length bytes without copying; negative or truncated ranges throw a format exception.</summary>
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

    /// <summary>Consumes one byte.</summary>
    public byte ReadByte() => Take(sizeof(byte))[0];
    /// <summary>Consumes a little-endian signed 16-bit integer.</summary>
    public short ReadInt16LE() => BinaryPrimitives.ReadInt16LittleEndian(Take(sizeof(short)));
    /// <summary>Consumes a little-endian unsigned 16-bit integer.</summary>
    public ushort ReadUInt16LE() => BinaryPrimitives.ReadUInt16LittleEndian(Take(sizeof(ushort)));
    /// <summary>Consumes a little-endian signed 32-bit integer.</summary>
    public int ReadInt32LE() => BinaryPrimitives.ReadInt32LittleEndian(Take(sizeof(int)));
    /// <summary>Consumes a little-endian unsigned 32-bit integer.</summary>
    public uint ReadUInt32LE() => BinaryPrimitives.ReadUInt32LittleEndian(Take(sizeof(uint)));
    /// <summary>Consumes a little-endian signed 64-bit integer.</summary>
    public long ReadInt64LE() => BinaryPrimitives.ReadInt64LittleEndian(Take(sizeof(long)));
    /// <summary>Consumes a little-endian unsigned 64-bit integer.</summary>
    public ulong ReadUInt64LE() => BinaryPrimitives.ReadUInt64LittleEndian(Take(sizeof(ulong)));
    /// <summary>Consumes a little-endian IEEE 754 single-precision value.</summary>
    public float ReadSingleLE() => BinaryPrimitives.ReadSingleLittleEndian(Take(sizeof(float)));
    /// <summary>Consumes a little-endian IEEE 754 double-precision value.</summary>
    public double ReadDoubleLE() => BinaryPrimitives.ReadDoubleLittleEndian(Take(sizeof(double)));

    /// <summary>Consumes a signed 32-bit count and rejects negative values or counts above the inclusive maximum.</summary>
    public int ReadCount(int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        var count = ReadInt32LE();
        if (count < 0 || count > maximum)
            throw new SaveFormatException($"{Context}: invalid count {count} at 0x{Position - sizeof(int):X}.");
        return count;
    }
}
