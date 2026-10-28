using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class YoteiResearchTests
{
    [Theory]
    [InlineData("sce_sdmemory", 13, 411619, 29902195)]
    [InlineData("suckerpunchAutoE1", 16, 411397, 30157398)]
    public void AdditiveHypothesisMatchesBothRangesButCannotEstablishPaddingCoverage(
        string directory, uint word1, uint size, uint checksum)
    {
        var file = SampleSaves.Load("PPSA26344").AllDiskFiles.Single(file => file.Directory.Name == directory &&
            file.FileName is "dis.dat" or "memory.dat");
        var bytes = file.ReadAllBytes();
        Assert.Equal((uint)0x150, Word(bytes, 0));
        Assert.Equal(word1, Word(bytes, 4));
        Assert.Equal(size, Word(bytes, 8));
        Assert.Equal(checksum, Word(bytes, 12));
        Assert.Equal((int)size, bytes.AsSpan().LastIndexOfAnyExcept((byte)0) + 1);
        var contentSum = Sum(bytes.AsSpan(16, (int)size - 16));
        var fixedFileSum = Sum(bytes.AsSpan(16));
        Assert.Equal(checksum, unchecked(contentSum + size + 0x150U));
        Assert.Equal(checksum, unchecked(fixedFileSum + size + 0x150U));
        Assert.NotEqual(checksum, unchecked(contentSum + size + 0x150U + word1));
    }

    [Fact]
    public void BothNonemptySamplesHaveTwoDifferentNamedRecordLayoutsWithZeroCounters()
    {
        var plugin = new SuckerPunchPlugin();
        foreach (var file in SampleSaves.Load("PPSA26344").AllDiskFiles.Where(file => plugin.Probe(file).IsMatch))
        {
            var bytes = file.ReadAllBytes();
            if (Word(bytes, 0) == 0)
            {
                continue;
            }

            ReadOnlySpan<byte> anchor = "\x30\0\0\0\x0b\0\0\0ArrowNormal"u8;
            var first = bytes.AsSpan().IndexOf(anchor);
            Assert.True(first >= 0);
            var secondRelative = bytes.AsSpan(first + anchor.Length).IndexOf(anchor);
            Assert.True(secondRelative >= 0);
            var second = first + anchor.Length + secondRelative;
            var firstNames = ReadNames(bytes, first, trailerLength: 21, zeroPrefixLength: 17);
            var secondNames = ReadNames(bytes, second, trailerLength: 16, zeroPrefixLength: 16);
            Assert.Equal(firstNames, secondNames);
            Assert.Contains("Mon", firstNames);
            Assert.Contains("Flowers", firstNames);
        }
    }

    [Fact]
    public void YoteiRemainsReadOnlyAndByteIdenticalRegardlessOfTheChecksumHypothesis()
    {
        var plugin = new SuckerPunchPlugin();
        foreach (var file in SampleSaves.Load("PPSA26344").AllDiskFiles.Where(file => plugin.Probe(file).IsMatch))
        {
            using var document = plugin.Open(file);
            var view = Assert.IsType<TabsNode>(document.BuildView());
            Assert.False(document.CanEdit);
            var header = Assert.IsType<SectionNode>(view.Tabs[0].Children[1]);
            Assert.All(header.Children, node => Assert.True(Assert.IsType<FieldNode>(node).Spec.ReadOnly));
            Assert.False(document.ApplyEdits([new ValueEdit("container/word1", FieldValue.Of(17L))]).Succeeded);
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    private static IReadOnlyList<string> ReadNames(byte[] bytes, int table, int trailerLength, int zeroPrefixLength)
    {
        var names = new List<string>();
        var position = table + sizeof(uint);
        const int recordCount = 48;
        for (var index = 0; index < recordCount; index++)
        {
            var length = checked((int)Word(bytes, position));
            Assert.InRange(length, 1, 64);
            position += sizeof(uint);
            names.Add(Encoding.ASCII.GetString(bytes, position, length));
            position += length;
            Assert.True(bytes.AsSpan(position, zeroPrefixLength).IndexOfAnyExcept((byte)0) < 0);
            position += trailerLength;
        }

        return names;
    }

    private static uint Sum(ReadOnlySpan<byte> bytes)
    {
        ulong sum = 0;
        foreach (var value in bytes)
        {
            sum += value;
        }

        return unchecked((uint)sum);
    }

    private static uint Word(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
