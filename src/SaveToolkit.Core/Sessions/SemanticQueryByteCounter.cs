namespace SaveToolkit.Core.Sessions;

internal sealed class SemanticQueryByteLimitException : IOException;

/// <summary>Counts full serialized pages, including read-only cells, without retaining a JSON copy.</summary>
internal sealed class SemanticQueryByteCounter(long limit, CancellationToken cancellationToken = default) : Stream
{
    public long BytesWritten { get; private set; }
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => BytesWritten;
    public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (buffer.Length > limit - BytesWritten) throw new SemanticQueryByteLimitException();
        BytesWritten += buffer.Length;
    }

    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
