using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class Dat1FormatTests
{
    [Theory]
    [InlineData("i29-userprefs.save", 4720, 1)]
    [InlineData("i29-s.save", 560776, 19)]
    public void RepresentativeHeadersMatchCorpus(string fileName, int size, int sections)
    {
        var file = SampleSaves.Load("PPSA01474").AllDiskFiles.First(file => file.FileName == fileName);
        using var document = new InsomniacPlugin().Open(file);
        _ = document.BuildView();
        Assert.Equal(FieldValue.Of((ulong)size), document.GetValue("header/total-size"));
        Assert.Equal(FieldValue.Of((long)sections), document.GetValue("header/section-count"));
        Assert.Equal(FieldValue.Of(8L), document.GetValue("header/word"));
    }

    [Fact]
    public void EverySuppliedDat1RecordOpensAndRoundTripsExactly()
    {
        var plugin = new InsomniacPlugin();
        var files = SampleSaves.Load("PPSA01474").AllDiskFiles
            .Where(file => plugin.Probe(file).IsMatch)
            .ToArray();

        Assert.Equal(76, files.Length);
        foreach (var file in files)
        {
            using var document = plugin.Open(file);
            Assert.True(document.CanEdit);
            var output = Assert.Single(document.Serialize());
            Assert.Equal(file.RelativePath, output.RelativePath);
            Assert.Equal(file.ReadAllBytes(), output.Content.ToArray());
        }
    }

    [Fact]
    public void DescriptorOrderAndPaddingArePreserved()
    {
        var original = Synthetic(sectionOffsetA: 0x50, sectionOffsetB: 0x40, padding: 0xA5);
        var plugin = new InsomniacPlugin();
        using var document = plugin.Open(MemorySaveSet.SingleFile("i29-s.save", original));
        var view = document.BuildView();
        Assert.Contains("Sections", Assert.IsType<SaveToolkit.Sdk.Views.TabsNode>(view).Tabs.Select(tab => tab.Title));
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("magic")]
    [InlineData("word")]
    [InlineData("size")]
    [InlineData("overlap")]
    [InlineData("truncated")]
    public void InvalidDat1IsRejected(string corruption)
    {
        var bytes = Synthetic(0x40, 0x50, 0);
        switch (corruption)
        {
            case "magic":
                BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0);
                break;
            case "word":
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 7);
                break;
            case "size":
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)(bytes.Length - 1));
                break;
            case "overlap":
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16 + 12 + 4), 0x42);
                break;
            case "truncated":
                bytes = bytes[..^1];
                break;
        }

        var file = MemorySaveSet.SingleFile("i29-s.save", bytes);
        Assert.Throws<SaveFormatException>(() => new InsomniacPlugin().Open(file));
    }

    private static byte[] Synthetic(uint sectionOffsetA, uint sectionOffsetB, byte padding)
    {
        const int firstSize = 4;
        const int secondSize = 3;
        var size = checked((int)Math.Max(sectionOffsetA + firstSize, sectionOffsetB + secondSize));
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x4441_5431);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), 0);
        WriteDescriptor(bytes, 16, 0xAAAABBBB, sectionOffsetA, firstSize);
        WriteDescriptor(bytes, 28, 0xCCCCDDDD, sectionOffsetB, secondSize);
        for (var index = 40; index < bytes.Length; index++) bytes[index] = padding;
        bytes[(int)sectionOffsetA] = 1;
        bytes[(int)sectionOffsetA + 1] = 2;
        bytes[(int)sectionOffsetA + 2] = 3;
        bytes[(int)sectionOffsetA + 3] = 4;
        bytes[(int)sectionOffsetB] = 5;
        bytes[(int)sectionOffsetB + 1] = 6;
        bytes[(int)sectionOffsetB + 2] = 7;
        return bytes;
    }

    private static void WriteDescriptor(byte[] bytes, int offset, uint tag, uint sectionOffset, uint size)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), tag);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 4), sectionOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 8), size);
    }
}
