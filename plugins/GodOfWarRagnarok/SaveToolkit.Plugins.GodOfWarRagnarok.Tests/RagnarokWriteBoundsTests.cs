using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokWriteBoundsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WalletCannotOverlapMetadataOrDirectory(bool directory)
    {
        const int originalWalletOffset = 0x500;
        const int metadataWalletOffset = 0x178;
        const int directoryCount = 10;
        var bytes = RagnarokTestSupport.LegacyGameplay();
        var wallet = bytes.AsSpan(originalWalletOffset,
            RagnarokFormat.WalletHeaderSize + RagnarokFormat.WalletRowSize).ToArray();
        bytes.AsSpan(originalWalletOffset, wallet.Length).Clear();
        var offset = metadataWalletOffset;
        if (directory)
        {
            // Put a complete wallet inside paired directory words. Neither array escapes
            // the file; only their ownership of the same bytes makes the layout invalid.
            bytes.AsSpan(RagnarokFormat.DirectoryItemsOffset,
                originalWalletOffset - RagnarokFormat.DirectoryItemsOffset).Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(RagnarokFormat.DirectoryCountOffset), directoryCount);
            for (var row = 0; row < directoryCount; row++)
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(RagnarokFormat.DirectoryItemsOffset + row * sizeof(ulong)),
                    (ulong)row + 1);
            offset = RagnarokFormat.DirectoryItemsOffset + directoryCount * sizeof(ulong) + sizeof(uint);
        }

        wallet.CopyTo(bytes, offset);
        AssertRejected(bytes);
    }

    [Fact]
    public void LegacyWalletRowsCannotConsumeTheTrailingSentinel()
    {
        const int originalWalletOffset = 0x500;
        var bytes = RagnarokTestSupport.LegacyGameplay();
        var wallet = bytes.AsSpan(originalWalletOffset,
            RagnarokFormat.WalletHeaderSize + RagnarokFormat.WalletRowSize).ToArray();
        bytes.AsSpan(originalWalletOffset, wallet.Length).Clear();
        wallet.CopyTo(bytes, bytes.Length - wallet.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(bytes.Length - sizeof(uint)), uint.MaxValue);
        AssertRejected(bytes);
    }

    [Fact]
    public void NestedFlagsCannotConsumeTheTrailingSentinel()
    {
        var original = RagnarokWriteTests.Sample().ReadAllBytes();
        var index = RagnarokRecordIndex.Parse(original, false);
        var bytes = original[..index.Nested.FlagsEnd];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(bytes.Length - sizeof(uint)), uint.MaxValue);
        AssertRejected(bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyWalletAtEitherExactBoundaryRemainsEditableAndLossless(bool beforeSentinel)
    {
        const int originalWalletOffset = 0x500;
        var bytes = RagnarokTestSupport.LegacyGameplay();
        var index = RagnarokRecordIndex.Parse(bytes, false);
        var wallet = bytes.AsSpan(originalWalletOffset,
            RagnarokFormat.WalletHeaderSize + RagnarokFormat.WalletRowSize).ToArray();
        bytes.AsSpan(originalWalletOffset, wallet.Length).Clear();
        var offset = beforeSentinel ? bytes.Length - sizeof(uint) - wallet.Length :
            index.SecondaryOffset + index.SecondaryCount * sizeof(ulong) + sizeof(uint);
        wallet.CopyTo(bytes, offset);
        using var document = (RagnarokDocument)new GodOfWarRagnarokPlugin().Open(
            MemorySaveSet.SingleFile("GOWRSAVE0", bytes, "PPSA08330"));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        var field = document.CounterField(offset + RagnarokFormat.WalletHeaderSize + sizeof(ulong));
        var result = document.ApplyEdits([new(field.ValueId, FieldValue.Of(0L))]);
        Assert.True(result.Succeeded);
        RagnarokWriteTests.AssertOnlyRangesChanged(bytes, Assert.Single(document.Serialize()).Content.ToArray(),
            (offset + RagnarokFormat.WalletHeaderSize + sizeof(ulong), sizeof(uint)));
        Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void NestedFlagsEndingImmediatelyBeforeSentinelRemainLossless()
    {
        var original = RagnarokWriteTests.Sample().ReadAllBytes();
        var index = RagnarokRecordIndex.Parse(original, false);
        var bytes = original[..(index.Nested.FlagsEnd + sizeof(uint))];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index.Nested.FlagsEnd), uint.MaxValue);
        using var document = new GodOfWarRagnarokPlugin().Open(
            MemorySaveSet.SingleFile("GOWRSAVE0", bytes, "PPSA08330"));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static void AssertRejected(byte[] bytes)
    {
        var file = MemorySaveSet.SingleFile("GOWRSAVE0", bytes, "PPSA08330");
        Assert.Throws<SaveFormatException>(() => new GodOfWarRagnarokPlugin().Open(file));
    }
}
