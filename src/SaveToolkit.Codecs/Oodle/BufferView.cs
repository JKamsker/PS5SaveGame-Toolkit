using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SaveToolkit.Codecs.Oodle;

/// <summary>
/// Checked, typed view over decoder-owned managed bytes. This preserves the upstream algorithm's
/// byte offsets and little-endian table layout without unmanaged pointers or native allocations.
/// Cursor arithmetic is checked; every dereference validates its complete extent. Padding for the
/// upstream word-at-a-time loops belongs to the allocation and never escapes to the caller.
/// </summary>
internal readonly struct BufferView<T> : IEquatable<BufferView<T>>
    where T : unmanaged
{
    private readonly byte[]? _buffer;
    private readonly int _offset;
    private readonly DecodeBudget? _budget;
    private readonly int _length;

    internal BufferView(byte[]? buffer, int offset, DecodeBudget? budget, int? length = null)
    {
        _buffer = buffer;
        _offset = offset;
        _budget = budget;
        _length = length ?? buffer?.Length ?? 0;
    }

    internal byte[] Buffer => _buffer ?? throw new InvalidDataException("Missing Kraken buffer.");
    internal int Offset => _offset;
    internal DecodeBudget Budget => _budget ?? throw new InvalidDataException("Missing decode budget.");
    private static int ElementSize => Unsafe.SizeOf<T>();

    internal ref T this[long index]
    {
        get
        {
            Budget.Spend();
            int position = checked(_offset + checked((int)(index * ElementSize)));
            return ref MemoryMarshal.Cast<byte, T>(Buffer.AsSpan(0, _length).Slice(position, ElementSize))[0];
        }
    }

    internal ref T Value => ref this[0];
    internal BufferView<U> As<U>() where U : unmanaged => new(_buffer, _offset, _budget, _length);
    internal Span<byte> Bytes(long count)
    {
        Budget.Spend(count);
        return Buffer.AsSpan(0, _length).Slice(_offset, checked((int)count));
    }

    internal static BufferView<T> Allocate(int count, DecodeBudget budget) =>
        new(new byte[checked(count * ElementSize)], 0, budget);

    public static BufferView<T> operator +(BufferView<T> view, long count) =>
        new(view._buffer, checked(view._offset + checked((int)(count * ElementSize))), view._budget, view._length);
    public static BufferView<T> operator -(BufferView<T> view, long count) => view + checked(-count);
    public static long operator -(BufferView<T> left, BufferView<T> right)
    {
        if (!ReferenceEquals(left._buffer, right._buffer))
            throw new InvalidDataException("Kraken cursor subtraction crosses buffers.");
        return ((long)left._offset - right._offset) / ElementSize;
    }
    public static BufferView<T> operator ++(BufferView<T> view) => view + 1;
    public static BufferView<T> operator --(BufferView<T> view) => view - 1;
    // Relative address comparisons are used upstream only to detect overlap. Distinct managed
    // allocations never overlap. Ordered scans always compare views of the same allocation.
    public static bool operator <(BufferView<T> a, BufferView<T> b) => ReferenceEquals(a._buffer, b._buffer) && a._offset < b._offset;
    public static bool operator >(BufferView<T> a, BufferView<T> b) => b < a;
    public static bool operator <=(BufferView<T> a, BufferView<T> b) => a == b || a < b;
    public static bool operator >=(BufferView<T> a, BufferView<T> b) => a == b || a > b;
    public static bool operator ==(BufferView<T> a, BufferView<T> b) => a.Equals(b);
    public static bool operator !=(BufferView<T> a, BufferView<T> b) => !a.Equals(b);
    public bool Equals(BufferView<T> other) => ReferenceEquals(_buffer, other._buffer) && _offset == other._offset;
    public override bool Equals(object? obj) => obj is BufferView<T> other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_buffer, _offset);
}
