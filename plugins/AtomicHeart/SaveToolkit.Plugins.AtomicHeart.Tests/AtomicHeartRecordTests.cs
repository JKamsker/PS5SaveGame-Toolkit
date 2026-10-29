using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartRecordTests
{
    [Fact]
    public void StoredFixtureHasExactArrayAndRecordBoundsAndRetainsNativeTail()
    {
        var plain = PlainFixture.Create(nativeTail: true, secondClassPath: true);
        var bytes = SlotFixture.Create(plain: plain);
        var index = SlotDataDecoder.Decode(bytes, SaveExtensionFile.Parse(bytes));
        Assert.Equal(plain, index.Plain);
        Assert.Equal(plain.Length - 4, BinaryPrimitives.ReadInt32LittleEndian(index.Plain));
        var record = Assert.Single(index.Objects);
        Assert.Equal("None", record.ClassPath);
        Assert.Equal("/Script/Test.Subsystem", record.ClassSubPath);
        Assert.Equal("SyntheticTag", Assert.Single(record.Tags));
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, plain.AsSpan(record.UnknownTail.Offset, record.UnknownTail.Length).ToArray());
        Assert.Equal(4, record.Properties.Single(p => p.Name == "Amount").Value!.Value.AsInt64());
        Assert.Equal(plain.Length, Assert.Single(index.Levels).Span.End + sizeof(int));
        using var document = SlotFixture.Open(bytes);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        var blobId = $"plain/{record.UnknownTail.Offset}/{record.UnknownTail.Length}";
        Assert.Equal(new byte[] { 0xBB, 0xCC }, document.ReadBlob(blobId, 1, int.MaxValue).ToArray());
        Assert.Throws<KeyNotFoundException>(() => document.ReadBlob("plain/2147483647/2", 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ReadBlob("plain", long.MaxValue, 1));
    }

    [Fact]
    public void EveryInnerTruncationWithValidCompressionAndHashIsRejected()
    {
        var plain = PlainFixture.Create();
        for (var length = 0; length < plain.Length; length++)
        {
            var truncated = plain[..length];
            // Keep every enclosing layer valid so rejection exercises the inner grammar.
            if (length >= sizeof(int))
                BinaryPrimitives.WriteInt32LittleEndian(truncated, length - sizeof(int));
            var error = Assert.Throws<SaveFormatException>(() => SlotFixture.Open(SlotFixture.Create(plain: truncated)));
            Assert.DoesNotContain("byte-array prefix", error.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public void DecodedArrayLengthCannotLieEvenWhenFramingAndHashAreValid(int length)
    {
        var plain = PlainFixture.Create();
        BinaryPrimitives.WriteInt32LittleEndian(plain, length);
        Assert.Throws<SaveFormatException>(() => SlotFixture.Open(SlotFixture.Create(plain: plain)));
    }

    [Fact]
    public void InvalidRootTagsCountsAndTrailingDataAreRejected()
    {
        var original = PlainFixture.Create();
        var parsed = new RecordReader(original).Read();
        var property = Assert.Single(parsed.Properties);
        foreach (var position in new[] { 4, property.Tag.Offset + 4 + "TestRoot\0".Length, property.Tag.End + 9, property.Tag.End + 13 })
        {
            var plain = original.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(plain.AsSpan(position), int.MaxValue);
            Assert.Throws<SaveFormatException>(() => SlotFixture.Open(SlotFixture.Create(plain: plain)));
        }
        var trailing = original.Concat(new byte[] { 0xFF }).ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(trailing, trailing.Length - 4);
        Assert.Throws<SaveFormatException>(() => SlotFixture.Open(SlotFixture.Create(plain: trailing)));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(10)]
    [InlineData(12)]
    public void OtherOodleVariantsAreRejectedWithoutFallback(int decoder)
    {
        var bytes = SlotFixture.Create();
        var parsed = SaveExtensionFile.Parse(bytes);
        bytes[parsed.DataOffset + SlotDataDecoder.FrameHeaderBytes + 1] = (byte)decoder;
        SlotFixture.RefreshDigest(bytes);
        var error = Assert.Throws<SaveFormatException>(() => SlotFixture.Open(bytes));
        Assert.Contains("Kraken", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MutationFailuresRemainFormatErrorsRatherThanUnhandledParserExceptions()
    {
        var original = PlainFixture.Create(nativeTail: true);
        var random = new Random(12695);
        for (var attempt = 0; attempt < 500; attempt++)
        {
            var plain = original.ToArray();
            plain[random.Next(plain.Length)] ^= (byte)random.Next(1, 256);
            try { using var document = SlotFixture.Open(SlotFixture.Create(plain: plain)); }
            catch (SaveFormatException) { /* Some mutations remain valid opaque data; rejection is not required. */ }
        }
    }

    [Fact]
    public void F1AlignsRecordIdentityAcrossPropertyRelocationAndDetectsOnlyTheChangedValue()
    {
        var decoder = new PluginViewDecoder(new AtomicHeartPlugin());
        var before = decoder.Decode(File(PlainFixture.Create()), new DecodeLimits());
        var after = decoder.Decode(File(PlainFixture.Create(amount: 5, reversed: true)), new DecodeLimits());
        var diff = SemanticDiffer.Compare(before, after);
        Assert.False(diff.InputTruncated);
        var change = Assert.Single(diff.Changes);
        Assert.EndsWith("/Amount[0]", change.Path, StringComparison.Ordinal);
        Assert.Equal("4", change.Before);
        Assert.Equal("5", change.After);
        Assert.Equal(0, diff.Added);
        Assert.Equal(0, diff.Removed);
    }

    [Fact]
    public void DuplicatePropertyNamesKeepDistinctBindingsAndValues()
    {
        var decoder = new PluginViewDecoder(new AtomicHeartPlugin());
        var tree = decoder.Decode(File(PlainFixture.Create(duplicateProperties: true)), new DecodeLimits());
        Assert.Empty(tree.Warnings);
        var amounts = tree.Values.Where(v => v.Path.Contains("/Amount[0]", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, amounts.Length);
        Assert.NotEqual(amounts[0].Path, amounts[1].Path);
        Assert.Equal("4", amounts[0].Value);
        Assert.Equal("5", amounts[1].Value);
    }

    [Fact]
    public void IncorrectPlainChunkLengthIsRejectedEvenWithMatchingDuplicateLengthsAndSha1()
    {
        var bytes = SlotFixture.Create();
        var outer = SaveExtensionFile.Parse(bytes);
        var declared = (ulong)(outer.Chunks[0].UncompressedBytes + 1);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(outer.DataOffset + 24), declared);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(outer.DataOffset + 40), declared);
        SlotFixture.RefreshDigest(bytes);
        Assert.Throws<SaveFormatException>(() => SlotFixture.Open(bytes));
    }

    private static Sdk.Saves.ISaveFile File(byte[] plain) => MemorySaveSet.SingleFile("slot", SlotFixture.Create(plain: plain), "PPSA10695");
}
