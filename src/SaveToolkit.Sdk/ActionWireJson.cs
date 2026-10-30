using System.Buffers;
using System.Text.Json;

namespace SaveToolkit.Sdk;

/// <summary>Exact capped wire measurement without allocating a complete serialized payload.</summary>
internal static class ActionWireJson
{
    public static long Measure<T>(T value, long maximum)
    {
        using var buffer = new Counter(maximum);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { MaxDepth = MetadataJson.MaxDepth }))
            JsonSerializer.Serialize(writer, value, MetadataJson.Options);
        return buffer.Written;
    }

    private sealed class Counter(long maximum) : IBufferWriter<byte>, IDisposable
    {
        private const int MaximumTemporarySpan = 16 * 1024 * 1024;
        private byte[]? _buffer;
        public long Written { get; private set; }
        public void Advance(int count)
        {
            if (count < 0 || count > maximum - Written)
                throw new InvalidOperationException("Serialized action data exceeds its bounded budget.");
            Written += count;
        }
        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            if (sizeHint < 0 || sizeHint > MaximumTemporarySpan)
                throw new InvalidOperationException("Serialized action data exceeds its bounded budget.");
            if (_buffer is null || _buffer.Length < Math.Max(1, sizeHint))
            {
                Dispose();
                _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(256, sizeHint));
            }
            return _buffer;
        }
        public Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;
        public void Dispose()
        {
            if (_buffer is not null) ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }
    }
}
