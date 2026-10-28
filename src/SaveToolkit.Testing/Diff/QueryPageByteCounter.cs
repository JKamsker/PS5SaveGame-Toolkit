namespace SaveToolkit.Testing.Diff;

internal sealed class QueryPageByteLimitException : IOException;

/// <summary>Counts serialized UTF-8 page bytes and stops serialization when its budget is exhausted.</summary>
internal sealed class QueryPageByteCounter(long limit) : Stream
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
        if (buffer.Length > limit - BytesWritten) throw new QueryPageByteLimitException();
        BytesWritten += buffer.Length;
    }

    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
