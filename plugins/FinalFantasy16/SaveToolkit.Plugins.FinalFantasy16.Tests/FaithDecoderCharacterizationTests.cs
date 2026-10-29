using SaveToolkit.Sdk.Diff;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithDecoderCharacterizationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(13)]
    [InlineData(31)]
    [InlineData(98)]
    [InlineData(99)]
    public void EvidenceRetainsOrderedPathsValuesAndImmediateSinkRefusal(int accepted)
    {
        var sink = new RecordingSink(accepted);
        new FaithEvidenceDecoder().Decode(FaithTestData.File(FaithCharacterizationData.Combined()), sink);

        var expected = new List<(string Path, string Value)>
        {
            ("player/level", "100"), ("player/xp", "101"),
            ("player/childLevel", "102"), ("player/childXp", "103"),
            ("user/playTime", "400"), ("inventory/0/item", "1"), ("inventory/0/quantity", "1234"),
            ("inventory/1/item", "3"), ("inventory/1/quantity", "567"),
            ("party/scene", "300"), ("party/map", "301"), ("party/id", "304"), ("party/count", "305"),
        };
        for (var index = 0; index < 18; index++)
            expected.Add(($"equipment/{index / 6}/{index % 6}/item", FaithCatalog.Key(200 + index)));
        for (var index = 0; index < 68; index++)
            expected.Add(($"skills/ranks/{index}", FaithCatalog.Key(index % 4)));
        Assert.Equal(99, expected.Count);
        Assert.Equal(expected.Take(accepted + 1), sink.Attempts);
    }

    private sealed class RecordingSink(int accepted) : ISaveDecodeSink
    {
        public List<(string Path, string Value)> Attempts { get; } = [];
        public int MaxValues => int.MaxValue;
        public int MaxValueLength => 256;
        public int MaxDepth => 64;
        public long MaxFileBytes => long.MaxValue;
        public bool Add(string path, string kind, string value, string? label = null)
        {
            Assert.Equal("integer", kind);
            Assert.Null(label);
            Attempts.Add((path, value));
            return Attempts.Count <= accepted;
        }
        public void Warn(string message) => Assert.Fail(message);
        public void MarkTruncated(string reason) => Assert.Fail(reason);
    }
}
