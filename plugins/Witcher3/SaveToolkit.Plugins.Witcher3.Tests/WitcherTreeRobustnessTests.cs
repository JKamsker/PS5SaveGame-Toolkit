using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherTreeRobustnessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndexedChildrenCannotHideMalformedArchive(bool corruptLength)
    {
        var archive = RobustnessSave.Archive(1);
        if (corruptLength) { RobustnessSave.WriteInt(archive, 2, int.MaxValue); }
        else { RobustnessSave.WriteInt(archive, 12, int.MaxValue); }
        var payload = RobustnessSave.Payload(archive, new(16, archive.Length), new(22, archive.Length - 6));
        Assert.Throws<SaveFormatException>(() => RobustnessSave.Open(payload));
    }

    [Theory]
    [InlineData(128, 0)]
    [InlineData(1, 127)]
    [InlineData(64, 64)]
    public void CombinedIndexedAndEmbeddedDepthCannotExceed128(int indexed, int blocks)
    {
        var archive = RobustnessSave.Archive(blocks);
        var variables = new byte[(indexed - 1) * 4 + archive.Length];
        var entries = new List<Sav3Entry>();
        for (var i = 0; i < indexed - 1; i++)
        {
            "BS\x01\0"u8.CopyTo(variables.AsSpan(i * 4));
            entries.Add(new(16 + i * 4, variables.Length - i * 4));
        }
        archive.CopyTo(variables, (indexed - 1) * 4);
        entries.Add(new(16 + (indexed - 1) * 4, archive.Length));
        var payload = RobustnessSave.Payload(variables, entries.ToArray());
        using var valid = RobustnessSave.Open(payload);

        // Adding one embedded level crosses the same total bound, regardless of the indexed split.
        var deeper = RobustnessSave.Archive(blocks + 1);
        variables = [.. variables.AsSpan(0, (indexed - 1) * 4), .. deeper];
        var deeperEntries = entries.Select(e => e with { Size = e.Size + 10 }).ToArray();
        Assert.Throws<SaveFormatException>(() => RobustnessSave.Open(RobustnessSave.Payload(variables, deeperEntries)));
    }

    [Theory]
    [InlineData("SXAP", 16)]
    [InlineData("BLCK", 10)]
    [InlineData("PORP", 12)]
    [InlineData("AVAL", 12)]
    public void EveryRecognizedEmbeddedHeaderTruncationHasAFormatError(string tag, int headerSize)
    {
        var magic = System.Text.Encoding.ASCII.GetBytes(tag);
        for (var length = magic.Length; length < headerSize; length++)
        {
            var archive = RobustnessSave.Archive(0);
            Array.Resize(ref archive, 6 + length);
            RobustnessSave.WriteInt(archive, 2, length);
            magic.CopyTo(archive, 6);
            var payload = RobustnessSave.Payload(archive, new Sav3Entry(16, archive.Length));
            Assert.Throws<SaveFormatException>(() => RobustnessSave.Open(payload));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void NestedBlockLengthsCannotEscapeTheirParent(int length)
    {
        var archive = RobustnessSave.Archive(3);
        RobustnessSave.WriteInt(archive, 6 + 10 + 6, length);
        var payload = RobustnessSave.Payload(archive, new Sav3Entry(16, archive.Length));
        Assert.Throws<SaveFormatException>(() => RobustnessSave.Open(payload));
    }

    [Fact]
    public void SiblingEmptyBlocksMakeProgressAndUnknownRemaindersRoundTrip()
    {
        var empty = RobustnessSave.Archive(1)[6..];
        byte[] archive = [.. RobustnessSave.Archive(0), .. empty, .. empty, .. "BLC"u8];
        RobustnessSave.WriteInt(archive, 2, archive.Length - 6);
        var payload = RobustnessSave.Payload(archive, new Sav3Entry(16, archive.Length));
        var bytes = SyntheticSave.Wrap(payload);
        var container = WitcherContainer.Parse(bytes);
        var tree = Sav3Tree.Parse(payload, Sav3Directory.Parse(container));
        var stream = Assert.Single(tree.Nodes, n => n.Tag == "SS");
        Assert.Equal(["BLCK", "BLCK", "Opaque"], stream.Children.Select(i => tree.Nodes[i].Tag));
        using var document = RobustnessSave.OpenContainer(bytes);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void ExpandedArchiveHonorsTheGlobalNodeBudget()
    {
        const int nodeLimit = 200_000;
        const int tokenSize = 16;
        var data = new byte[6 + nodeLimit * tokenSize];
        "SS"u8.CopyTo(data);
        RobustnessSave.WriteInt(data, 2, data.Length - 6);
        for (var offset = 6; offset < data.Length; offset += tokenSize) { "SXAP"u8.CopyTo(data.AsSpan(offset)); }
        var directory = new Sav3Directory([], [new(0, data.Length)], data.Length);
        var error = Assert.Throws<SaveFormatException>(() => Sav3Tree.Parse(data, directory));
        Assert.Contains("node limit", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("BLCK", 10, 6)]
    [InlineData("PORP", 12, 8)]
    [InlineData("AVAL", 12, 8)]
    public void IndexedLengthBearingTokensMustMatchTheirSpans(string tag, int headerSize, int lengthOffset)
    {
        var variables = new byte[headerSize + 4];
        System.Text.Encoding.ASCII.GetBytes(tag).CopyTo(variables, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(variables.AsSpan(4), 1);
        if (tag != "BLCK") { System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(variables.AsSpan(6), 3); }
        RobustnessSave.WriteInt(variables, lengthOffset, 5);
        Assert.Throws<SaveFormatException>(() => RobustnessSave.Open(
            RobustnessSave.Payload(variables, new Sav3Entry(16, variables.Length))));
    }
}
