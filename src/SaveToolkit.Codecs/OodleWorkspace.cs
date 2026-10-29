using System.Buffers;
using SaveToolkit.Codecs.Oodle;

namespace SaveToolkit.Codecs;

/// <summary>Owns reusable pooled buffers for independent Kraken streams. Dispose after the batch.</summary>
/// <remarks>Use one workspace per operation; concurrent or reentrant use is rejected.</remarks>
public sealed class OodleWorkspace : IDisposable
{
    private byte[]? _source;
    private byte[]? _destination;
    private byte[]? _scratch;
    private int _state;
    private const int Idle = 0;
    private const int InUse = 1;
    private const int Disposed = 2;

    internal void Acquire()
    {
        int previous = Interlocked.CompareExchange(ref _state, InUse, Idle);
        ObjectDisposedException.ThrowIf(previous == Disposed, this);
        if (previous != Idle)
            throw new InvalidOperationException("The Kraken workspace is already in use.");
    }

    internal void Release() => Volatile.Write(ref _state, Idle);

    internal byte[] Source(int length) => Prepare(ref _source, length);
    internal byte[] Destination(int length) => Prepare(ref _destination, length);

    internal BufferView<byte> Scratch(int length, DecodeBudget budget) =>
        new(Prepare(ref _scratch, length), 0, budget, length);

    private static byte[] Prepare(ref byte[]? buffer, int length)
    {
        if (buffer is null || buffer.Length < length)
        {
            byte[] replacement = ArrayPool<byte>.Shared.Rent(length);
            Return(ref buffer);
            buffer = replacement;
        }
        // Match fresh zero-filled allocations, including padding and any data left by a failed decode.
        buffer.AsSpan(0, length).Clear();
        return buffer;
    }

    private static void Return(ref byte[]? buffer)
    {
        if (buffer is null) return;
        ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        buffer = null;
    }

    /// <summary>Clear and return all owned buffers to the pool. Repeated disposal is harmless.</summary>
    public void Dispose()
    {
        int previous = Interlocked.CompareExchange(ref _state, Disposed, Idle);
        if (previous == Disposed) return;
        if (previous == InUse)
            throw new InvalidOperationException("Cannot dispose a Kraken workspace while it is in use.");
        Return(ref _source);
        Return(ref _destination);
        Return(ref _scratch);
    }
}
