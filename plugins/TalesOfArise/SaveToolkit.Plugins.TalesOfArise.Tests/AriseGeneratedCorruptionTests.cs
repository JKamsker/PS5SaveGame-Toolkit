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
    private const string EnvelopeError = "size, magic or PS5 encryption mode";
    private const long MaximumRejectionAllocation = 8 * 1024 * 1024;

    public static IEnumerable<object[]> SectionIndices() => Enumerable.Range(0, AriseSchema.Sections.Count)
        .Select(index => new object[] { index });

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
    [InlineData(4, 0x7FFFFFFFu)]
    [InlineData(4, 0x80000000u)]
    [InlineData(4, uint.MaxValue)]
    public void InvalidDirectoryMarkerOrCount_WithValidSha1_IsRejected(int relative, uint value)
    {
        var fixture = new AriseTestContainer();
        fixture.Write(AriseTestContainer.DirectoryOffset + relative, value);
        AssertBoundedRejection(fixture.Encrypt(), DirectoryError);
    }

    [Theory]
    [MemberData(nameof(SectionIndices))]
    public void EverySectionHeaderField_RejectsInvalidWordsWithoutAllocatingFromThem(int index)
    {
        foreach (var relative in new[] { AriseTestContainer.SectionVersionOffset, AriseTestContainer.SectionIdOffset, AriseTestContainer.SectionSizeOffset })
        {
            foreach (var value in new uint[] { 0, int.MaxValue, 0x80000000, uint.MaxValue })
            {
                var fixture = new AriseTestContainer();
                fixture.Write(fixture.Header(index) + relative, value);
                AssertBoundedRejection(fixture.Encrypt(), $"section {fixture.Sections[index].Name} {LayoutError}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(SectionIndices))]
    public void ValidSectionHeader_WithoutItsCompleteBody_IsRejected(int index)
    {
        foreach (var availableBodyBytes in new[] { 0, AriseSchema.Sections[index].Size - AriseFormat.HeaderSize })
        {
            var fixture = new AriseTestContainer();
            var header = fixture.Payload.Length - AriseFormat.HeaderSize - availableBodyBytes;
            var link = index == 0 ? AriseTestContainer.FirstSectionPointerOffset
                : fixture.Header(index - 1) + AriseTestContainer.NextSectionOffset;
            fixture.Write(link, (uint)header);
            AssertRejected(fixture.EncryptThroughHeader(index, header), BlockError);
        }
    }

    [Theory]
    [InlineData(0u, BlockError)]
    [InlineData(0x20u, BlockError)]
    [InlineData(0x60u, BlockError)]
    [InlineData(0x70u, BlockError)]
    [InlineData(0xA1u, BlockError)]
    [InlineData(0x7FFFFFF0u, BlockError)]
    [InlineData(0x80000000u, PointerError)]
    [InlineData(uint.MaxValue, PointerError)]
    public void InvalidFirstSectionPointer_IsRejectedBeforeReadingItsHeader(uint value, string reason)
    {
        var fixture = new AriseTestContainer();
        fixture.Write(AriseTestContainer.FirstSectionPointerOffset, value);
        AssertBoundedRejection(fixture.Encrypt(), reason);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData((uint)(AriseFormat.FileSize - AriseFormat.PayloadOffset - 1))]
    [InlineData((uint)(AriseFormat.FileSize - AriseFormat.PayloadOffset + 1))]
    [InlineData(0x7FFFFFFFu)]
    [InlineData(0x80000000u)]
    [InlineData(uint.MaxValue)]
    public void InvalidDeclaredPayloadLength_IsRejectedWithoutAllocatingFromIt(uint value)
    {
        var bytes = new AriseTestContainer().Encrypt();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(AriseFormat.PayloadOffset + 4), value);
        AssertBoundedRejection(bytes, EnvelopeError);
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

    [Fact]
    public void Sha1_CoversEveryCipherRegionBoundary_AndEveryStoredDigestByte()
    {
        var fixture = new AriseTestContainer();
        var original = fixture.Encrypt();
        var cipherBoundaries = new[]
        {
            0x20, 0x5F, AriseTestContainer.DirectoryOffset, AriseTestContainer.DirectoryOffset + 0x3F,
            fixture.Payload.Length - 1,
        }.Concat(fixture.Sections.SelectMany(section => new[]
        {
            section.Offset - AriseFormat.HeaderSize, section.Offset - 1,
            section.Offset, section.Offset + section.Size - 1,
        })).Select(offset => AriseFormat.PayloadOffset + offset);
        foreach (var offset in cipherBoundaries.Concat(Enumerable.Range(AriseFormat.HashOffset, 20)).Distinct())
        {
            var bytes = original.ToArray();
            bytes[offset] ^= 0x80;
            AssertRejected(bytes, "SHA1 mismatch");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(AriseFormat.PayloadOffset - 1)]
    [InlineData(AriseFormat.ModeOffset + 2)]
    [InlineData(AriseFormat.ModeOffset + 3)]
    public void OpaqueOuterAndAlignmentBytes_AreOutsideSha1AndPreserved(int offset)
    {
        var bytes = new AriseTestContainer().Encrypt();
        bytes[offset] ^= 0x80;
        Assert.Equal(bytes, AriseFormat.Encode(AriseFormat.Decode(bytes)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(255)]
    public void FinalPlaintextBlock_IsPreservedWithoutPkcsPaddingInterpretation(byte value)
    {
        var fixture = new AriseTestContainer();
        var last = fixture.Sections[^1];
        fixture.Payload.AsSpan(last.Offset + last.Size - 16, 16).Fill(value);
        var bytes = fixture.Encrypt();
        var decoded = AriseFormat.Decode(bytes);
        Assert.Equal(fixture.Payload.AsSpan(last.Offset, last.Size).ToArray(), decoded.Payload.AsSpan(last.Offset, last.Size).ToArray());
        Assert.Equal(bytes, AriseFormat.Encode(decoded));
    }

    [Theory]
    [MemberData(nameof(SectionIndices))]
    public void CiphertextHeaderDamage_WithRepairedSha1_RejectsAtTheDamagedSection(int index)
    {
        var fixture = new AriseTestContainer();
        var original = fixture.Encrypt();
        for (var relative = 0; relative < AriseFormat.HeaderSize; relative++)
        {
            var bytes = original.ToArray();
            bytes[AriseFormat.PayloadOffset + fixture.Header(index) + relative] ^= 0x80;
            AriseSampleTests.Hash(bytes.AsSpan(AriseFormat.CipherOffset)).CopyTo(bytes, AriseFormat.HashOffset);
            AssertRejected(bytes, $"section {fixture.Sections[index].Name} {LayoutError}");
        }
    }

    private static void AssertBoundedRejection(byte[] bytes, string reason)
    {
        AssertRejected(bytes, reason); // Warm up the rejection path before measuring allocations.
        var before = GC.GetAllocatedBytesForCurrentThread();
        AssertRejected(bytes, reason);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // A fixed-size container needs only a few bounded copies, regardless of declared lengths.
        Assert.InRange(allocated, 0, MaximumRejectionAllocation);
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
