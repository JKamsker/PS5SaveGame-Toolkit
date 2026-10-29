using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokNestedCorruptionTests
{
    private readonly RagnarokNestedFixture _fixture = new();

    [Fact]
    public void SyntheticNestedRecordsConsumeTheirParentsAndRoundTripExactly()
    {
        var bytes = _fixture.Create();
        var index = RagnarokRecordIndex.Parse(bytes, false);
        Assert.Equal(3, index.Nested.Wallets.Count);
        Assert.All(index.Nested.Wallets, wallet =>
        {
            Assert.Equal(1, wallet.ResourceCount);
            Assert.Equal(1, wallet.IdCount);
            Assert.Equal(1, wallet.IndexCount);
        });
        Assert.Equal(_fixture.WalletBlockEnd, index.Nested.WalletBlockEnd);
        Assert.Equal(_fixture.WalletBlockEnd + sizeof(ushort) + sizeof(uint), index.Nested.LinksOffset);
        Assert.Equal(1, index.Nested.LinksCount);
        Assert.Equal(_fixture.FlagsLengthOffset, index.Nested.FlagsOffset);
        Assert.Equal(_fixture.FlagsEnd, index.Nested.FlagsEnd);
        Assert.Equal(2, index.Nested.FlagGroups.Count);
        Assert.All(index.Nested.FlagGroups, group => Assert.Equal(1, group.Count));
        using var document = new GodOfWarRagnarokPlugin().Open(File(bytes));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void EveryPayloadTruncationFailsEvenWithAValidReplacementSentinel()
    {
        var bytes = _fixture.Create();
        // Preserve the sentinel so these cases exercise structural parsing, not just EOF magic.
        for (var length = 0; length < _fixture.FlagsEnd; length++)
        {
            var truncated = new byte[length + sizeof(uint)];
            bytes.AsSpan(0, length).CopyTo(truncated);
            Write(truncated, length, uint.MaxValue);
            var file = File(truncated);
            _ = new GodOfWarRagnarokPlugin().Probe(file);
            Assert.Throws<SaveFormatException>(() => new GodOfWarRagnarokPlugin().Open(file));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AllWalletCountsRejectOversizedOrParentCrossingArrays(int field)
    {
        foreach (var wallet in new[] { 0, 1, 2 })
        foreach (var count in new[] { 100u, (uint)RagnarokFormat.MaximumRecordCount + 1, uint.MaxValue })
        {
            var bytes = _fixture.Create();
            Write(bytes, _fixture.WalletOffset(wallet) + sizeof(ulong) + field * sizeof(uint), count);
            AssertRejected(bytes, wallet == 0 && field == 0 ? "Invalid or truncated record array" : "Nested Ragnarök");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void WalletArraysCannotConsumeTheFollowingLinkOrFlagBlocks(int field)
    {
        var bytes = _fixture.Create();
        // These arrays fit the file but extend past the wallet block's own boundary.
        Write(bytes, _fixture.WalletOffset(2) + sizeof(ulong) + field * sizeof(uint), 5);
        AssertRejected(bytes, "Nested Ragnarök record crosses its enclosing byte range");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(uint.MaxValue)]
    public void WalletLengthCannotTruncateTheHeaderOrEscapeTheFile(uint length)
    {
        var bytes = _fixture.Create();
        Write(bytes, RagnarokNestedFixture.FirstWalletOffset - sizeof(uint), length);
        AssertRejected(bytes, "Nested Ragnarök");
    }

    [Fact]
    public void DuplicateNonHeroWalletAndFlagIdentitiesAreRejected()
    {
        var bytes = _fixture.Create();
        bytes.AsSpan(_fixture.WalletOffset(1), sizeof(ulong)).CopyTo(bytes.AsSpan(_fixture.WalletOffset(2)));
        AssertRejected(bytes, "Duplicate or excessive nested Ragnarök wallet identities");
        bytes = _fixture.Create();
        bytes.AsSpan(_fixture.GroupOffset(0), sizeof(ulong)).CopyTo(bytes.AsSpan(_fixture.GroupOffset(1)));
        AssertRejected(bytes, "Duplicate nested Ragnarök flag-group identity");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(27)]
    [InlineData(ushort.MaxValue)]
    public void LinkLengthMustExactlyConsumeItsCountedRows(ushort length)
    {
        var bytes = _fixture.Create();
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(_fixture.WalletBlockEnd), length);
        AssertRejected(bytes, "Nested Ragnarök");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(65537)]
    [InlineData(uint.MaxValue)]
    public void LinkCountMustBeBoundedAndAgreeWithLength(uint count)
    {
        var bytes = _fixture.Create();
        Write(bytes, _fixture.WalletBlockEnd + sizeof(ushort), count);
        AssertRejected(bytes, "Nested Ragnarök");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(ushort.MaxValue)]
    public void FlagLengthMustExactlyConsumeItsPrefixAndGroups(ushort length)
    {
        var bytes = _fixture.Create();
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(_fixture.FlagsLengthOffset), length);
        AssertRejected(bytes, "Nested Ragnarök");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void FlagPrefixIsValidated(int relativeOffset)
    {
        var bytes = _fixture.Create();
        bytes[_fixture.FlagsPayloadOffset + relativeOffset] ^= 1;
        AssertRejected(bytes, "Unsupported nested Ragnarök flag-group prefix");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(257)]
    [InlineData(uint.MaxValue)]
    public void FlagGroupCountMustBeBoundedAndConsumeTheBlock(uint count)
    {
        var bytes = _fixture.Create();
        Write(bytes, _fixture.FlagsPayloadOffset + sizeof(ulong) + sizeof(uint), count);
        AssertRejected(bytes, "Ragnarök");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(65537)]
    [InlineData(uint.MaxValue)]
    public void FlagRowCountCannotEscapeItsParent(uint count)
    {
        var bytes = _fixture.Create();
        Write(bytes, _fixture.GroupOffset(1) + sizeof(ulong), count);
        AssertRejected(bytes, "Nested Ragnarök");
    }

    [Theory]
    [InlineData(true, 256)]
    [InlineData(false, 256)]
    public void MaximumContainerCountRemainsLossless(bool wallets, int count)
    {
        var fixture = new RagnarokNestedFixture(wallets ? count : 3, wallets ? 2 : count);
        var bytes = fixture.Create();
        var index = RagnarokRecordIndex.Parse(bytes, false);
        Assert.Equal(count, wallets ? index.Nested.Wallets.Count : index.Nested.FlagGroups.Count);
        using var document = new GodOfWarRagnarokPlugin().Open(File(bytes));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExcessiveContainerCountIsRejected(bool wallets)
    {
        var bytes = new RagnarokNestedFixture(wallets ? 257 : 3, wallets ? 2 : 257).Create();
        AssertRejected(bytes, wallets ? "excessive nested Ragnarök wallet" : "Excessive nested Ragnarök flag-group");
    }

    private static void AssertRejected(byte[] bytes, string message)
    {
        var error = Assert.Throws<SaveFormatException>(() => new GodOfWarRagnarokPlugin().Open(File(bytes)));
        Assert.Contains(message, error.Message);
    }

    private static SaveToolkit.Sdk.Saves.ISaveFile File(byte[] bytes) =>
        MemorySaveSet.SingleFile("GOWRSAVE0", bytes, "PPSA08330");

    private static void Write(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
