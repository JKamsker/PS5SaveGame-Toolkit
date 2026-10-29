using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithDecoderBoundsTests
{
    private readonly FaithEvidenceDecoder _decoder = new();

    [Theory]
    [InlineData(FaithContainer.MaximumFileSize + 1L, long.MaxValue)]
    [InlineData(long.MaxValue, long.MaxValue)]
    [InlineData(1024L, 1023L)]
    public void OversizedFilesAreRejectedWithoutReadingTheirBody(long length, long hostLimit)
    {
        var file = new UnreadableBodyFile(length);
        var error = Record.Exception(() => _decoder.Decode(file, new FaithDecodeTestSink { MaxFileBytes = hostLimit }));
        Assert.NotNull(error);
        Assert.True(error is SaveFormatException or InvalidDataException, $"Expected a format/limit rejection, got {error}");
        Assert.False(file.BodyRead);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    private sealed class UnreadableBodyFile(long length) : ISaveFile
    {
        private readonly ISaveFile _headerFile = FaithTestData.File(FaithTestData.Container((99, 1, [])));
        public bool BodyRead { get; private set; }
        public string Id => _headerFile.Id;
        public ISaveDirectory Directory => _headerFile.Directory;
        public string RelativePath => _headerFile.RelativePath;
        public string PathInDirectory => _headerFile.PathInDirectory;
        public string FileName => _headerFile.FileName;
        public long Length => length;
        public Stream OpenRead() => throw new InvalidOperationException("The oversized body must not be read.");
        public byte[] ReadHeader(int count) => _headerFile.ReadHeader(count);
        public byte[] ReadAllBytes()
        {
            BodyRead = true;
            throw new InvalidOperationException("The oversized body must not be read.");
        }
    }
}
