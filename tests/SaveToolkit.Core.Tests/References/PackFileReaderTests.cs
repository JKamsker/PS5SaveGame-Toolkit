using SaveToolkit.Core.References;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests.References;

[Collection(typeof(AllocationTestCollection))]
public sealed class PackFileReaderTests
{
    [Fact]
    public void SeekableStreamIsReadExactly()
    {
        var data = Enumerable.Range(0, 200_000).Select(i => (byte)i).ToArray();

        var read = PackFileReader.ReadBounded(new MemoryStream(data, writable: false), data.Length, "'x'");

        Assert.Equal(data, read);
    }

    [Fact]
    public void SeekableStreamReadsFromCurrentPosition()
    {
        var stream = new MemoryStream([1, 2, 3, 4, 5], writable: false) { Position = 2 };

        Assert.Equal([3, 4, 5], PackFileReader.ReadBounded(stream, 3, "'x'"));
    }

    [Fact]
    public void NonSeekableStreamIsReadCompletely()
    {
        var data = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7)).ToArray();

        var read = PackFileReader.ReadBounded(new ForwardOnlyStream(data), data.Length, "'x'");

        Assert.Equal(data, read);
    }

    [Fact]
    public void LimitIsEnforcedForBothStreamKinds()
    {
        var data = new byte[1_001];

        Assert.Throws<ReferencePackException>(() => PackFileReader.ReadBounded(new MemoryStream(data), 1_000, "'x'"));
        Assert.Throws<ReferencePackException>(() => PackFileReader.ReadBounded(new ForwardOnlyStream(data), 1_000, "'x'"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsDoNotAllocateScratchBuffersPerFile(bool seekable)
    {
        // Neither embedded resources nor ZIP entry streams should pay for an 80 KB scratch chunk per file.
        var small = new byte[512];
        PackFileReader.ReadBounded(Open(), small.Length, "'warm-up'");

        var perFile = AllocationProbe.Measure(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                PackFileReader.ReadBounded(Open(), small.Length, "'x'");
            }
        }).TransformSamples(bytes => bytes / 100);
        perFile.AssertLessThan(4_096, $"Allocated {perFile.MinimumBytes:N0} bytes per 512-byte file.");

        Stream Open() => seekable ? new MemoryStream(small, writable: false) : new ForwardOnlyStream(small);
    }

    private sealed class ForwardOnlyStream(byte[] data) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(Math.Min(count, 4_000), data.Length - _position);
            Array.Copy(data, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
