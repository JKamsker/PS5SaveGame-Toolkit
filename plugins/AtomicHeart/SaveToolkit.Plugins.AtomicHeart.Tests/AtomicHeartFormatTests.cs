using System.Buffers.Binary;
using System.Diagnostics;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartFormatTests
{
    [Fact]
    public void SyntheticFramingRoundTripsAndProbingRequiresTitleAndContent()
    {
        var bytes = SlotFixture.Create();
        var plugin = new AtomicHeartPlugin();
        Assert.False(plugin.Probe(MemorySaveSet.SingleFile("ue4savegame.dpx.sav", bytes)).IsMatch);
        var file = MemorySaveSet.SingleFile("renamed.bin", bytes, "PPSA10695");
        Assert.Equal(ProbeScore.Title, plugin.Probe(file).Score);
        using var document = plugin.Open(file);
        Assert.False(document.CanEdit);
        var field = Assert.IsType<FieldNode>(Assert.Single(document.ExpandNode("metadata/0")));
        Assert.Equal((Int128)2, field.Value.AsInteger());
        Assert.True(field.Spec.ReadOnly);
        Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of(3L))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Equal(50, document.ReadBlob("data", 0, int.MaxValue).Length);
        Assert.Empty(document.ReadBlob("data", 50, 2).ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ReadBlob("data", -1, 1));
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode("chunks/-1"));
    }

    [Fact]
    public void Sha1MatchesPublishedAbcVector()
    {
        Assert.Equal("A9993E364706816ABA3E25717850C26C9CD0D89D", Convert.ToHexString(SlotFixture.Digest("abc"u8)));
    }

    [Fact]
    public void EmptyMetadataHasAValidExpandableView()
    {
        using var document = Open(SlotFixture.Create(includeMetadata: false));
        _ = document.BuildView();
        Assert.Empty(document.ExpandNode("metadata/0"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1025)]
    public void EmptyOrExcessiveChunkListsAreRejected(int chunks) =>
        Assert.Throws<SaveFormatException>(() => Open(SlotFixture.Create(chunks: chunks)));

    [Fact]
    public void CorruptionAndEveryTruncationAreRejected()
    {
        var bytes = SlotFixture.Create();
        for (var length = 0; length < bytes.Length; length++)
            Assert.Throws<SaveFormatException>(() => Open(bytes[..length]));
        for (var index = 0; index < bytes.Length; index++)
        {
            var corrupt = bytes.ToArray(); corrupt[index] ^= 1;
            Assert.Throws<SaveFormatException>(() => Open(corrupt));
        }
    }

    [Fact]
    public void InvalidLengthsAndChunkHeadersAreRejectedEvenWithValidHash()
    {
        var bytes = SlotFixture.Create();
        var chunk = bytes.Length - 20 - 50;
        foreach (var offset in new[] { 0, 4, chunk, chunk + 8, chunk + 16, chunk + 24, chunk + 32, chunk + 40, chunk + 48 })
        {
            var corrupt = bytes.ToArray(); corrupt[offset] ^= 0xFF; SlotFixture.RefreshDigest(corrupt);
            Assert.Throws<SaveFormatException>(() => Open(corrupt));
        }
        var huge = bytes.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(huge.AsSpan(22), int.MaxValue);
        SlotFixture.RefreshDigest(huge);
        Assert.Throws<SaveFormatException>(() => Open(huge));
    }

    [Fact]
    public void AllRealSnapshotsHaveKnownMetadataChunkSizesAndValidatedDigest()
    {
        var files = SampleSaves.Load("PPSA10695").AllDiskFiles.Where(f => !f.IsSystemFile).ToArray();
        Assert.NotEmpty(files);
        Assert.Single(files.DistinctBy(f => f.Sha256));
        foreach (var file in files)
        {
            var bytes = file.ReadAllBytes();
            Assert.Equal(126214, bytes.Length);
            Assert.Equal(bytes.AsSpan(^20).ToArray(), SlotFixture.Digest(bytes.AsSpan(0, bytes.Length - 20)));
            var timer = Stopwatch.StartNew();
            using var document = new AtomicHeartPlugin().Open(file);
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2));
            var fields = document.ExpandNode("metadata/0").OfType<FieldNode>().ToDictionary(f => f.Label);
            Assert.Equal("9", fields["Filename"].Value.AsString());
            Assert.Equal("Vavilov_Entrance", fields["SubLevel"].Value.AsString());
            Assert.Equal((Int128)2, fields["Version"].Value.AsInteger());
            Assert.Equal((Int128)34646201172L, fields["PlayedTime"].Value.AsInteger());
            var table = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("chunks/0")));
            Assert.Equal(14, table.Rows.Count);
            Assert.Equal(123892, document.ReadBlob("data", 0, int.MaxValue).Length);
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
            bytes[2302 + 48 + 2] ^= 1;
            Assert.Throws<SaveFormatException>(() => Open(bytes));
        }
    }

    private static ISaveDocument Open(byte[] bytes) => new AtomicHeartPlugin().Open(MemorySaveSet.SingleFile("save", bytes));
}
