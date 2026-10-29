using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class ChunkCorruptionTests
{
    private const int FirstEntry = CsavCorruptionFixture.TableOffset;
    private const int SecondEntry = FirstEntry + CsavCorruptionFixture.EntrySize;

    [Theory]
    [InlineData(29, -1, "chunk count")]
    [InlineData(29, 0, "chunk count")]
    [InlineData(29, 4097, "chunk count")]
    [InlineData(29, int.MaxValue, "chunk count")]
    [InlineData(FirstEntry, -1, "bounds")]
    [InlineData(FirstEntry, 45, "bounds")]
    [InlineData(FirstEntry, int.MaxValue, "bounds")]
    [InlineData(FirstEntry + 4, -1, "bounds")]
    [InlineData(FirstEntry + 4, 0, "bounds")]
    [InlineData(FirstEntry + 4, int.MaxValue, "bounds")]
    [InlineData(FirstEntry + 8, -1, "expanded size")]
    [InlineData(FirstEntry + 8, 0, "expanded size")]
    [InlineData(FirstEntry + 8, int.MaxValue, "expanded size")]
    [InlineData(SecondEntry, 58, "bounds")]
    [InlineData(SecondEntry + 4, int.MaxValue, "bounds")]
    [InlineData(SecondEntry + 8, int.MaxValue, "expanded size")]
    public void CorruptChunkWordsAreRejectedBeforeDecoding(int offset, int value, string diagnostic)
    {
        var bytes = Fixture(compressed: true);
        CsavCorruptionFixture.SetWord(bytes, offset, value);
        Assert.Contains(diagnostic, Assert.Throws<SaveFormatException>(() => CsavContainer.Parse(bytes)).Message);
    }

    [Fact]
    public void CumulativeExpansionOverBudgetIsRejectedEvenWhenEachChunkFits()
    {
        var bytes = Fixture(compressed: true);
        var firstOffset = BinaryData.Int32(bytes, FirstEntry);
        var secondOffset = BinaryData.Int32(bytes, SecondEntry);
        CsavCorruptionFixture.SetWord(bytes, FirstEntry + 8, BinaryData.MaxExpandedSize);
        CsavCorruptionFixture.SetWord(bytes, firstOffset + 4, BinaryData.MaxExpandedSize);
        CsavCorruptionFixture.SetWord(bytes, SecondEntry + 8, 1);
        CsavCorruptionFixture.SetWord(bytes, secondOffset + 4, 1);
        // Both wrappers agree, but the sum must be rejected before allocating/decoding the first block.
        Assert.Contains("chunk 1 bounds or expanded size",
            Assert.Throws<SaveFormatException>(() => CsavContainer.Parse(bytes)).Message);
    }

    [Theory]
    [InlineData(4096, true)]
    [InlineData(4097, false)]
    public void ChunkCountBoundIsCheckedWithCompleteTables(int count, bool accepted)
    {
        var payload = new byte[count];
        var bytes = CsavCorruptionFixture.Create(payload, [new("x", -1, -1, 0, count)],
            Enumerable.Repeat(1, count).ToArray());
        if (!accepted)
        {
            Assert.Contains("chunk count", Assert.Throws<SaveFormatException>(() => CsavContainer.Parse(bytes)).Message);
            return;
        }
        var container = CsavContainer.Parse(bytes);
        Assert.Equal(count, container.Chunks.Count);
        Assert.Equal(payload, container.Payload);
        Assert.Single(CsavTree.Parse(container).Nodes);
        Assert.Equal(bytes, container.Rebuild(container.Payload));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(8)]
    public void TruncatedCompressedWrapperIsRejectedBeforeReadingItsLength(int stored)
    {
        var bytes = Fixture(compressed: true);
        CsavCorruptionFixture.SetWord(bytes, FirstEntry + 4, stored);
        Assert.Contains("length declarations disagree", Assert.Throws<SaveFormatException>(() => CsavContainer.Parse(bytes)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoredWrapperAndDecodedLengthDeclarationsMustAgree(bool compressed)
    {
        var bytes = Fixture(compressed);
        CsavCorruptionFixture.SetWord(bytes, FirstEntry + 8, 3);
        Assert.Contains("length declarations disagree", Assert.Throws<SaveFormatException>(() => CsavContainer.Parse(bytes)).Message);
        if (!compressed) return;
        var offset = BinaryData.Int32(bytes, FirstEntry);
        CsavCorruptionFixture.SetWord(bytes, offset + 4, 3);
        Assert.Contains("LZ4 block or expanded length mismatch", Assert.Throws<SaveFormatException>(() => CsavContainer.Parse(bytes)).Message);
        CsavCorruptionFixture.SetWord(bytes, FirstEntry + 8, 5);
        CsavCorruptionFixture.SetWord(bytes, offset + 4, 5);
        Assert.Contains("LZ4 block or expanded length mismatch", Assert.Throws<SaveFormatException>(() => CsavContainer.Parse(bytes)).Message);
    }

    [Theory]
    [InlineData(0x00)] // Invalid zero-distance match.
    [InlineData(0xF0)] // Literal extension/sequence exceeds this block.
    [InlineData(0x30)] // Three literals followed by an incomplete match offset.
    public void InvalidLz4SequencesUseTheDocumentedRejection(byte token)
    {
        var bytes = Fixture(compressed: true);
        var offset = BinaryData.Int32(bytes, FirstEntry);
        bytes[offset + 8] = token;
        Assert.Contains("LZ4", Assert.Throws<SaveFormatException>(() => CsavContainer.Parse(bytes)).Message);
    }

    [Fact]
    public void TerminalLiteralSequenceDoesNotInterpretTheUnusedMatchNibble()
    {
        var bytes = Fixture(compressed: true);
        var offset = BinaryData.Int32(bytes, FirstEntry);
        bytes[offset + 8] = 0x4F;
        var container = CsavContainer.Parse(bytes);
        Assert.Equal(2, CsavTree.Parse(container).Nodes.Count);
        Assert.Equal(bytes, container.Rebuild(container.Payload));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryChunkTruncationWithIntactDirectoryAndFooterIsRejected(bool compressed)
    {
        var bytes = Fixture(compressed);
        var directory = BinaryData.Int32(bytes, bytes.Length - 8);
        var data = BinaryData.Int32(bytes, FirstEntry);
        for (var end = data; end < directory; end++)
        {
            byte[] truncated = [.. bytes.AsSpan(0, end), .. bytes.AsSpan(directory)];
            CsavCorruptionFixture.SetWord(truncated, truncated.Length - 8, end);
            Assert.Throws<SaveFormatException>(() => CsavContainer.Parse(truncated));
        }
        var container = CsavContainer.Parse(bytes);
        Assert.Equal(2, CsavTree.Parse(container).Nodes.Count);
        Assert.Equal(bytes, container.Rebuild(container.Payload));
    }

    private static byte[] Fixture(bool compressed)
    {
        byte[] payload = [0, 0, 0, 0, 1, 0, 0, 0];
        FixtureNode[] nodes = [new("a", 1, -1, 0, 4), new("b", -1, -1, 4, 4)];
        return CsavCorruptionFixture.Create(payload, nodes, [4, 4], compressed);
    }
}
