using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class InputLengthTests
{
    private const long MaximumFileSize = 64 * 1024 * 1024;

    [Theory]
    [InlineData(-1L)]
    [InlineData(15L)]
    [InlineData(MaximumFileSize + 1)]
    [InlineData(long.MaxValue)]
    public void InvalidLengthsRejectBeforeReadingTheBodyAtBothEntryPoints(long length)
    {
        var file = new UnreadableBodyFile(length);
        Assert.Contains("DAT1 file size", Assert.Throws<SaveFormatException>(() => new InsomniacPlugin().Open(file)).Message);
        Assert.False(file.BodyRead);
        Assert.Contains("DAT1 file size", Assert.Throws<SaveFormatException>(() =>
            new Insomniac.RiftEvidenceDecoder().Decode(file, new RobustnessTestSink())).Message);
        Assert.False(file.BodyRead);
    }

    [Fact]
    public void HostFileLimitStillRejectsBeforeReading()
    {
        var file = new UnreadableBodyFile(1024);
        Assert.Throws<InvalidDataException>(() => new Insomniac.RiftEvidenceDecoder().Decode(file, new RobustnessTestSink { MaxFileBytes = 1023 }));
        Assert.False(file.BodyRead);
    }

    private sealed class UnreadableBodyFile(long length) : ISaveFile
    {
        private readonly ISaveFile _file = MemorySaveSet.SingleFile("i29-s.save", new DdlTestFixture().Bytes);
        public bool BodyRead { get; private set; }
        public string Id => _file.Id;
        public ISaveDirectory Directory => _file.Directory;
        public string RelativePath => _file.RelativePath;
        public string PathInDirectory => _file.PathInDirectory;
        public string FileName => _file.FileName;
        public long Length => length;
        public byte[] ReadHeader(int count) => _file.ReadHeader(count);
        public Stream OpenRead() => throw new InvalidOperationException("Unexpected body read.");
        public byte[] ReadAllBytes() { BodyRead = true; throw new InvalidOperationException("Unexpected body read."); }
    }
}
