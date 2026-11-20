using System.Text.Json;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Core;

namespace SaveToolkit.LocalApi.Protocol;

/// <summary>Serializes a collection page only when the complete page fits the response budget.</summary>
internal static class CollectionPageSerializer
{
    public const int MaximumBytes = 2 * 1024 * 1024;
    public const string LimitMessage = "The collection page exceeds the response size limit.";

    /// <returns>The UTF-8 JSON page, or null when it exceeds <see cref="MaximumBytes"/>.</returns>
    public static ReadOnlyMemory<byte>? TrySerialize(QueryablePageDto page)
    {
        var body = new LimitedStream();
        try
        {
            // The stream serializer flushes incrementally. Keep the bytes private until the
            // entire page fits, so an oversized response can still return a clean 413 envelope.
            JsonSerializer.Serialize(body, page, ToolkitJson.Options);
            return body.Content;
        }
        catch (ResponseLimitException)
        {
            return null;
        }
    }

    private sealed class ResponseLimitException : Exception;

    private sealed class LimitedStream : Stream
    {
        private readonly MemoryStream _buffer = new();

        public ReadOnlyMemory<byte> Content => _buffer.GetBuffer().AsMemory(0, (int)_buffer.Length);
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _buffer.Length;
        public override long Position { get => _buffer.Position; set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length > MaximumBytes - _buffer.Length) throw new ResponseLimitException();
            _buffer.Write(buffer);
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _buffer.Dispose();
            base.Dispose(disposing);
        }
    }
}
