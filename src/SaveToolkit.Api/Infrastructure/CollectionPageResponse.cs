using System.Text.Json;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Core;

namespace SaveToolkit.Api.Infrastructure;

internal static class CollectionPageResponse
{
    internal const int MaximumBytes = 2 * 1024 * 1024;

    internal static IResult Create(QueryablePageDto page)
    {
        using var body = new LimitedStream();
        try
        {
            // The stream serializer flushes incrementally. Keep the bytes private until the
            // entire page fits, so an oversized response can still return a clean 413 envelope.
            JsonSerializer.Serialize(body, page, ToolkitJson.Options);
            return Results.Bytes(body.Content, "application/json");
        }
        catch (ResponseLimitException)
        {
            return Results.Json(new ErrorDto("The collection page exceeds the response size limit."),
                statusCode: StatusCodes.Status413PayloadTooLarge, options: ToolkitJson.Options);
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
