using System.Buffers.Binary;
using SaveToolkit.Codecs.Sqlite;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Plugins.UnrealGvas.Hogwarts;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class HogwartsMalformedImageTests
{
    [Theory]
    [InlineData("page-size")]
    [InlineData("wal")]
    [InlineData("btree")]
    [InlineData("truncated")]
    public void CorruptSqliteImagesDisableOnlyTheBadOverlayAndPreserveBytes(string corruption)
    {
        byte[] image = Image();
        switch (corruption)
        {
            case "page-size": BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(16), 0); break;
            case "wal": image[18] = 2; break;
            case "btree": image[100] = 0xFF; break;
            case "truncated": image = image[..^1]; break;
        }
        Assert.Throws<InvalidDataException>(() => new HogwartsDatabase("bad", image));
        AssertFailedOverlay(TitleFixture.Hogwarts(HogwartsImage.EncodeStored(image), HogwartsImage.EncodeStored(Image())));
    }

    [Fact]
    public void TruncatedFramePrefixesAndPayloadsRejectCleanly()
    {
        byte[] frames = HogwartsImage.EncodeStored(Image());
        int[] lengths = [.. Enumerable.Range(0, HogwartsImage.FrameBytes + 1), frames.Length / 2, frames.Length - 1];
        foreach (int length in lengths)
            Assert.Throws<SaveFormatException>(() => HogwartsImage.Decode(frames.AsSpan(0, length)));
    }

    [Theory]
    [InlineData("magic")]
    [InlineData("length")]
    [InlineData("codec")]
    public void InvalidFramesDisableTheOverlayWithAnExplicitWarning(string corruption)
    {
        byte[] frames = HogwartsImage.EncodeStored(Image());
        switch (corruption)
        {
            case "magic": frames[0] ^= 1; break;
            case "length": BinaryPrimitives.WriteUInt64LittleEndian(frames.AsSpan(24), ulong.MaxValue); break;
            case "codec": frames[HogwartsImage.FrameBytes] = 0; break;
        }
        AssertFailedOverlay(TitleFixture.Hogwarts(frames, HogwartsImage.EncodeStored(Image())));
    }

    private static void AssertFailedOverlay(byte[] bytes)
    {
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("save.sav", bytes, "PPSA01603"));
        Assert.False(document.CanEdit);
        var tabs = Assert.IsType<TabsNode>(document.BuildView());
        var warning = Assert.Single(tabs.Tabs.First().Children.OfType<TextNode>(), text =>
            text.Text.StartsWith("RawDatabaseImage: database validation failed", StringComparison.Ordinal));
        Assert.Equal(Tone.Warning, warning.Tone);
        Assert.Contains("overlay unavailable", warning.Text);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        Assert.DoesNotContain(queryable.Collections, collection => collection.Id.StartsWith("hogwarts/RawDatabaseImage/", StringComparison.Ordinal));
        var good = Assert.Single(queryable.Collections);
        Assert.StartsWith("hogwarts/RawExclusiveImage/", good.Id);
        Assert.Single(queryable.Query(good.Id, new()).Rows);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static byte[] Image()
    {
        using var source = SqliteImage.Create();
        source.Execute("CREATE TABLE InventoryDynamic(Count INTEGER); INSERT INTO InventoryDynamic VALUES(1);");
        return source.Serialize();
    }
}
