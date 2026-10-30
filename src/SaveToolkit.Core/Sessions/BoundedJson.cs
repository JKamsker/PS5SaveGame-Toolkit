using System.Buffers;
using System.Text.Json;

namespace SaveToolkit.Core.Sessions;

/// <summary>Serializes retained planning data without first allocating an unbounded byte array.</summary>
internal static class BoundedJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        // Effect specs admit the SDK's bounded depth (128); the serializer needs headroom for
        // the enclosing plan/descriptor graph without turning this into an unbounded walker.
        MaxDepth = SaveToolkit.Sdk.MetadataJson.MaxDepth,
    };

    public static byte[] Serialize<T>(T value, int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        var writer = new CappedWriter(maximum);
        using (var json = new Utf8JsonWriter(writer, new JsonWriterOptions { MaxDepth = Options.MaxDepth })) JsonSerializer.Serialize(json, value, Options);
        return writer.ToArray();
    }

    // A finite pooled scratch span measures wire bytes before retained graph copies. A long
    // escaped string can request more space than it ultimately writes; the actual cap is inclusive.
    public static long Measure<T>(T value, int maximum)
    {
        if (maximum < 0) throw new InvalidOperationException("Serialized action data exceeds its bounded budget.");
        using var buffer = new MeasuringWriter(maximum);
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { MaxDepth = Options.MaxDepth }))
            JsonSerializer.Serialize(json, value, Options);
        return buffer.Written;
    }

    private sealed class MeasuringWriter(int maximum) : IBufferWriter<byte>, IDisposable
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

    private sealed class CappedWriter(int maximum) : IBufferWriter<byte>
    {
        // Utf8JsonWriter asks for the complete escaped span of a large UTF-16 string even when
        // the resulting UTF-8 payload remains within the cap. Keep a bounded request cushion;
        // Advance still enforces the actual retained-byte ceiling.
        private const int BufferSlack = 16 * 1024 * 1024;
        private readonly ArrayBufferWriter<byte> _inner = new(Math.Min(maximum, 4096));
        private readonly int _maximum = maximum;

        public void Advance(int count)
        {
            if (count < 0 || _inner.WrittenCount > _maximum - count)
                throw new InvalidOperationException("Serialized action data exceeds its bounded budget.");
            _inner.Advance(count);
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Validate(sizeHint);
            return _inner.GetMemory(Math.Max(Math.Min(sizeHint, _maximum - _inner.WrittenCount + BufferSlack), 1));
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Validate(sizeHint);
            return _inner.GetSpan(Math.Max(Math.Min(sizeHint, _maximum - _inner.WrittenCount + BufferSlack), 1));
        }

        public byte[] ToArray() => _inner.WrittenSpan.ToArray();

        private void Validate(int sizeHint)
        {
            if (sizeHint < 0)
                throw new InvalidOperationException("Serialized action data exceeds its bounded budget.");
        }
    }
}
