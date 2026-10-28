using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokNestedTests
{
    private static readonly int[] LateResourceCounts = [211, 10, 8, 0, 3];
    [Fact]
    public void EveryPrimaryGenerationHasExactlyConsumedNestedBlocksAndLosslessReopen()
    {
        var files = SampleSaves.Load("PPSA08330").AllDiskFiles
            .Where(file => !file.Directory.IsBackup && file.FileName.StartsWith("GOWRSAVE", StringComparison.Ordinal)).ToArray();
        Assert.Equal(76, files.Length);
        foreach (var file in files)
        {
            var bytes = file.ReadAllBytes();
            var index = RagnarokRecordIndex.Parse(bytes, false);
            var nested = index.Nested;
            Assert.Equal(5, nested.Wallets.Count);
            Assert.Equal(RagnarokFormat.HeroWalletId, nested.Wallets[0].Id);
            Assert.Equal(nested.WalletBlockEnd, nested.Wallets[^1].End);
            Assert.Equal(5, nested.FlagGroups.Count);
            Assert.Equal(132, nested.FlagGroups.Sum(group => group.Count));
            Assert.Equal(nested.FlagsEnd, nested.FlagGroups[^1].End);
            var cursor = 0;
            foreach (var span in index.Evidence.Spans.Where(span => span.Length > 0))
            {
                Assert.Equal(cursor, span.Offset);
                cursor += span.Length;
            }

            Assert.Equal(bytes.Length, cursor);
            using var document = new GodOfWarRagnarokPlugin().Open(file);
            var exported = Assert.Single(document.Serialize()).Content.ToArray();
            using var reopened = new GodOfWarRagnarokPlugin().Open(MemorySaveSet.SingleFile(file.FileName, exported, "PPSA08330"));
            Assert.False(reopened.CanEdit);
            Assert.Equal(bytes, Assert.Single(reopened.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void NestedLengthsCountsAndIdentitiesCannotEscapeTheirParents()
    {
        var bytes = SampleSaves.Load("PPSA08330").AllDiskFiles.First(file => file.FileName == "GOWRSAVE0").ReadAllBytes();
        var index = RagnarokRecordIndex.Parse(bytes, false);
        var nested = index.Nested;
        var offsets = new[]
        {
            nested.WalletBlockOffset, index.WalletOffset + 12, index.WalletOffset + 16,
            nested.WalletBlockEnd, nested.LinksOffset - sizeof(uint), nested.FlagsOffset,
            nested.FlagsOffset + sizeof(ushort) + sizeof(ulong),
            nested.FlagGroups[0].Offset + sizeof(ulong),
        };
        foreach (var offset in offsets)
        {
            var corrupt = bytes.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(corrupt.AsSpan(offset), uint.MaxValue);
            Assert.Throws<SaveFormatException>(() => RagnarokRecordIndex.Parse(corrupt, false));
        }

        var duplicate = bytes.ToArray();
        // Duplicate a non-hero container so the outer hero-marker uniqueness check cannot mask this case.
        duplicate.AsSpan(nested.Wallets[1].Offset, sizeof(ulong)).CopyTo(duplicate.AsSpan(nested.Wallets[2].Offset));
        Assert.Throws<SaveFormatException>(() => RagnarokRecordIndex.Parse(duplicate, false));

        duplicate = bytes.ToArray();
        duplicate.AsSpan(nested.FlagGroups[0].Offset, sizeof(ulong)).CopyTo(duplicate.AsSpan(nested.FlagGroups[1].Offset));
        Assert.Throws<SaveFormatException>(() => RagnarokRecordIndex.Parse(duplicate, false));
    }

    [Fact]
    public void KnownLateSaveHasSeparateContainersAndUnknownIndexLikeWords()
    {
        var bytes = SampleSaves.Load("PPSA08330").AllDiskFiles.First(file => file.FileName == "GOWRSAVE0").ReadAllBytes();
        var nested = RagnarokRecordIndex.Parse(bytes, false).Nested;
        Assert.Equal(LateResourceCounts, nested.Wallets.Select(wallet => wallet.ResourceCount));
        Assert.Equal(261, nested.Wallets[0].IdCount);
        Assert.Equal(159, nested.Wallets[0].IndexCount);
        Assert.Equal(0x1D5CC, nested.WalletBlockEnd);
        Assert.Equal(0x1DC50, nested.FlagsEnd);
    }
}
