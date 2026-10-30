using System.Buffers;
using System.Text.Json;

namespace SaveToolkit.Core.Sessions;

/// <summary>Serializes retained planning data without first allocating an unbounded byte array.</summary>
internal static class BoundedJson
{
    public static byte[] Serialize<T>(T value, int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        var writer = new CappedWriter(maximum);
        using (var json = new Utf8JsonWriter(writer)) JsonSerializer.Serialize(json, value);
        return writer.ToArray();
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
