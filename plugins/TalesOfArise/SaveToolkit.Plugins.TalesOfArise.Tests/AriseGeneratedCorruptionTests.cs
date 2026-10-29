using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseGeneratedCorruptionTests
{
    private const string BlockError = "unaligned, overlapping or out of bounds";
    private const string PointerError = "section pointer is out of bounds";
    private const string DirectoryError = "section directory";
    private const string LayoutError = "layout";
    private const string TailError = "unexpected tail";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedContainer_TraversesEverySection_AndRoundTripsExactly(bool reversePhysicalOrder)
    {
        var fixture = new AriseTestContainer(reversePhysicalOrder);
        var bytes = fixture.Encrypt();
        var save = AriseFormat.Decode(bytes);
        Assert.Equal(30, save.Sections.Count);
        Assert.Equal(138_832 * sizeof(uint), save.Sections.Sum(section => section.Size));
        Assert.Equal(fixture.Sections, save.Sections);
        Assert.Equal(fixture.Payload.AsSpan(0x20).ToArray(), save.Payload.AsSpan(0x20).ToArray());
        Assert.Equal(bytes, AriseFormat.Encode(save));
        using var document = new TalesOfArisePlugin().Open(File(bytes));
        _ = document.BuildView();
        Assert.Equal(123, document.GetValue("party/gald").AsInt64());
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(AriseFormat.PayloadOffset, "size, magic or PS5 encryption mode")]
    [InlineData(AriseFormat.PayloadOffset + 4, "size, magic or PS5 encryption mode")]
    [InlineData(AriseFormat.ModeOffset, "size, magic or PS5 encryption mode")]
    [InlineData(AriseFormat.ModeOffset + 1, "size, magic or PS5 encryption mode")]
    [InlineData(AriseFormat.HashOffset, "SHA1 mismatch")]
    [InlineData(AriseFormat.CipherOffset, "SHA1 mismatch")]
    [InlineData(AriseFormat.FileSize - 1, "SHA1 mismatch")]
    public void CorruptEnvelope_RejectsAtTheExpectedValidationStage(int offset, string reason)
    {
        var bytes = new AriseTestContainer().Encrypt();
        bytes[offset] ^= 0x80;
        AssertRejected(bytes, reason);
    }

    [Theory]
    [InlineData(0u, BlockError)]
    [InlineData(0x20u, BlockError)]
    [InlineData(0x61u, BlockError)]
    [InlineData(0x7FFFFFF0u, BlockError)]
    [InlineData(0x80000000u, PointerError)]
    [InlineData(uint.MaxValue, PointerError)]
    public void InvalidDirectoryPointer_WithValidSha1_IsRejected(uint pointer, string reason)
    {
        var fixture = new AriseTestContainer();
        fixture.Write(AriseTestContainer.DirectoryPointerOffset, pointer);
        AssertRejected(fixture.Encrypt(), reason);
    }

    [Theory]
    [InlineData(0, 99u)]
    [InlineData(4, 0u)]
    [InlineData(4, 29u)]
    [InlineData(4, 31u)]
    [InlineData(4, uint.MaxValue)]
    public void InvalidDirectoryMarkerOrCount_WithValidSha1_IsRejected(int relative, uint value)
    {
        var fixture = new AriseTestContainer();
        fixture.Write(AriseTestContainer.DirectoryOffset + relative, value);
        AssertRejected(fixture.Encrypt(), DirectoryError);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(29)]
    public void EverySectionHeaderField_IsValidatedAtFirstMiddleAndLastSection(int index)
    {
        foreach (var relative in new[] { AriseTestContainer.SectionVersionOffset, AriseTestContainer.SectionIdOffset, AriseTestContainer.SectionSizeOffset })
        {
            var fixture = new AriseTestContainer();
            fixture.Write(fixture.Header(index) + relative, uint.MaxValue);
            AssertRejected(fixture.Encrypt(), $"section {fixture.Sections[index].Name} {LayoutError}");
        }
    }

    [Theory]
    [InlineData(0, "early-end", BlockError)]
    [InlineData(15, "early-end", BlockError)]
    [InlineData(0, "self", BlockError)]
    [InlineData(15, "self", BlockError)]
    [InlineData(15, "previous", BlockError)]
    [InlineData(0, "body", BlockError)]
    [InlineData(15, "body", BlockError)]
    [InlineData(0, "unaligned", BlockError)]
    [InlineData(15, "outside", BlockError)]
    [InlineData(15, "overflow", PointerError)]
    [InlineData(29, "self", TailError)]
    public void MalformedSectionLinks_WithValidSha1_RejectAtTheExpectedStage(int index, string mutation, string reason)
    {
        var fixture = new AriseTestContainer();
        var pointer = mutation switch
        {
            "early-end" => 0u,
            "self" => (uint)fixture.Header(index),
            "previous" => (uint)fixture.Header(index - 1),
            "body" => (uint)fixture.Sections[index].Offset,
            "unaligned" => (uint)fixture.Header(index + 1) + 1,
            "outside" => (uint)fixture.Payload.Length,
            "overflow" => uint.MaxValue,
            _ => throw new ArgumentException("Unknown mutation.", nameof(mutation)),
        };
        fixture.Write(fixture.Header(index) + AriseTestContainer.NextSectionOffset, pointer);
        AssertRejected(fixture.Encrypt(), reason);
    }

    private static void AssertRejected(byte[] bytes, string reason)
    {
        // Calling Open covers the public boundary, including ciphertext integrity verification.
        var exception = Assert.Throws<SaveFormatException>(() => new TalesOfArisePlugin().Open(File(bytes)));
        Assert.Contains(reason, exception.Message, StringComparison.Ordinal);
    }

    private static SaveToolkit.Sdk.Saves.ISaveFile File(byte[] bytes) =>
        MemorySaveSet.SingleFile(AriseTestContainer.FileName, bytes, AriseTestContainer.TitleId);
}
