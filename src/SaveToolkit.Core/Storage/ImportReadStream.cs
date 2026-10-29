namespace SaveToolkit.Core.Storage;

/// <summary>Observes cancellation during ZIP directory parsing as well as entry decompression.</summary>
internal sealed class ImportReadStream(Stream source, CancellationToken cancellationToken) : Stream
{
    public override bool CanRead => source.CanRead;
    public override bool CanSeek => source.CanSeek;
    public override bool CanWrite => false;
    public override long Length => source.Length;
    public override long Position { get => source.Position; set => Seek(value, SeekOrigin.Begin); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int read = source.Read(buffer);
        cancellationToken.ThrowIfCancellationRequested();
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return source.Seek(offset, origin);
    }

    public override void Flush() => source.Flush();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) source.Dispose();
        base.Dispose(disposing);
    }
}
