using System.Buffers.Binary;
using SaveToolkit.Sdk.Views;
using static SaveToolkit.Plugins.SuckerPunch.Tests.YoteiTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

/// <summary>Pins candidate coverage without treating plugin acceptance as evidence of game acceptance.</summary>
public sealed class YoteiIntegrityCoverageTests
{
    private const int HeaderSize = 16;
    private const int DeclaredEndOffset = 8;
    private const int ChecksumOffset = 12;
    private const int QuantityInTrailer = 5;
    private const int PayloadLength = 616_448;
    private const uint Magic = 0x150;

    [Fact]
    public void CorpusHasElevenDistinctNonemptyPayloadsAndTwoZeroPlaceholders()
    {
        var files = Files;
        Assert.Equal(13, files.Count);
        var nonemptyHashes = new HashSet<string>(StringComparer.Ordinal);
        var placeholders = 0;
        foreach (var file in files)
        {
            var bytes = file.ReadAllBytes();
            Assert.Equal(PayloadLength, bytes.Length);
            if (Word(bytes, 0) == 0)
            {
                Assert.True(bytes.AsSpan().IndexOfAnyExcept((byte)0) < 0);
                placeholders++;
            }
            else
                Assert.True(nonemptyHashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))));
        }
        Assert.Equal(11, nonemptyHashes.Count);
        Assert.Equal(2, placeholders);
    }

    [Theory]
    [InlineData("sce_sdmemory", 414213U, 30214928U)]
    [InlineData("suckerpunchAuto0", 414182U, 30193082U)]
    [InlineData("suckerpunchAuto1", 414332U, 30234516U)]
    [InlineData("suckerpunchAuto3", 412284U, 29988190U)]
    [InlineData("suckerpunchAuto4", 413377U, 30080227U)]
    [InlineData("suckerpunchAutoE1", 411397U, 30157398U)]
    [InlineData("suckerpunchUser0", 412308U, 29994398U)]
    [InlineData("suckerpunchUser1", 413162U, 30064194U)]
    [InlineData("suckerpunchUser2", 414023U, 30176692U)]
    [InlineData("suckerpunchUser3", 414156U, 30199339U)]
    [InlineData("suckerpunchUser4", 417135U, 30420001U)]
    public void SampleVectorsCannotDistinguishDeclaredEndFromWholeFileCoverage(string slot, uint end, uint checksum)
    {
        var bytes = Slot(slot).ReadAllBytes();
        Assert.Equal(Magic, Word(bytes, 0));
        Assert.Equal(end, Word(bytes, DeclaredEndOffset));
        Assert.Equal(checksum, Word(bytes, ChecksumOffset));
        Assert.Equal((int)end, bytes.AsSpan().LastIndexOfAnyExcept((byte)0) + 1);
        Assert.True(bytes.AsSpan((int)end).IndexOfAnyExcept((byte)0) < 0);
        Assert.Equal(checksum, Checksum(bytes));
        Assert.Equal(checksum, WholeFileHypothesis(bytes));
    }

    [Fact]
    public void EveryWritableQuantityAndAmmoCopyByteIsInsideTheCandidateInterval()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Files)
        {
            var original = file.ReadAllBytes();
            if (Word(original, 0) == 0) continue;
            using var document = Open(file);
            var records = InventoryRecords(original).ToDictionary(row => row.Name, row => row.Data);
            foreach (var row in Inventory(document).Rows)
            {
                if (Assert.IsType<FieldNode>(row.Cells["quantity"]).Spec.ReadOnly) continue;
                names.Add(row.Key);
                var start = records[row.Key] + QuantityInTrailer;
                var length = row.Cells["copy"] is FieldNode ? 2 * sizeof(uint) : sizeof(uint);
                Assert.InRange(start, HeaderSize, checked((int)Word(original, DeclaredEndOffset)) - length);
                for (var offset = start; offset < start + length; offset++)
                {
                    var mutated = original.ToArray();
                    mutated[offset] ^= 1;
                    var expected = unchecked(Word(original, ChecksumOffset) - original[offset] + mutated[offset]);
                    Assert.Equal(expected, Checksum(mutated));
                    Assert.Equal(expected, WholeFileHypothesis(mutated));
                    // No repair: the production checksum gate must notice every potentially written byte.
                    using var mismatched = Open(WithBytes(mutated));
                    Assert.False(mismatched.CanEdit);
                }
            }
        }
        Assert.Equal(14, names.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonzeroTailSeparatesHypothesesButOnlyEstablishesPluginPolicy(bool lastByte)
    {
        foreach (var file in Files)
        {
            var bytes = file.ReadAllBytes();
            if (Word(bytes, 0) == 0) continue;
            using var original = Open(file);
            var checksum = Word(bytes, ChecksumOffset);
            var offset = lastByte ? bytes.Length - 1 : checked((int)Word(bytes, DeclaredEndOffset));
            bytes[offset] = 1; // In-memory counterexample, never a naturally observed or console-tested tail.
            Assert.Equal(checksum, Checksum(bytes));
            Assert.Equal(checksum + 1, WholeFileHypothesis(bytes));
            using (var declaredEndCandidate = Open(WithBytes(bytes)))
            {
                Assert.Equal(original.CanEdit, declaredEndCandidate.CanEdit);
                Assert.Equal(bytes, Assert.Single(declaredEndCandidate.Serialize()).Content.ToArray());
            }
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(ChecksumOffset), WholeFileHypothesis(bytes));
            using var wholeFileCandidate = Open(WithBytes(bytes));
            Assert.False(wholeFileCandidate.CanEdit);
            Assert.Equal(bytes, Assert.Single(wholeFileCandidate.Serialize()).Content.ToArray());
        }
    }

    private static uint WholeFileHypothesis(byte[] bytes)
    {
        ulong sum = Magic + Word(bytes, DeclaredEndOffset);
        foreach (var value in bytes.AsSpan(HeaderSize)) sum += value;
        return unchecked((uint)sum);
    }
}
