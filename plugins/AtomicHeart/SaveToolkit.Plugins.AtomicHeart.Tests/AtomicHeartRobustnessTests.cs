using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartRobustnessTests
{
    [Fact]
    public void InventoryActorComponentAndNestedPayloadTruncationsRejectBeyondTheAggregatePrefix()
    {
        var bytes = InventoryFixture.Create();
        var plain = SlotDataDecoder.Decode(bytes, SaveExtensionFile.Parse(bytes)).Plain;
        // The earlier PlainFixture test has no actors, components or nested collections.
        for (var length = 0; length < plain.Length; length++)
        {
            var truncated = plain[..length];
            if (length >= sizeof(int)) BinaryPrimitives.WriteInt32LittleEndian(truncated, length - sizeof(int));
            var error = Assert.Throws<SaveFormatException>(() => Open(SlotFixture.Create(plain: truncated)));
            Assert.DoesNotContain("byte-array prefix", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CollectionElementTruncationsReachTheirGrammarWithAllEnclosingLengthsRepaired()
    {
        var bytes = InventoryFixture.Create();
        var index = SlotDataDecoder.Decode(bytes, SaveExtensionFile.Parse(bytes));
        var component = Assert.Single(index.Objects, record => record.Kind == RecordKinds.Component);
        var container = Assert.Single(component.Properties);
        var slots = Assert.Single(container.Children);
        var descriptorStart = slots.Payload.Offset + sizeof(int);
        var descriptorSizeOffset = SizeOffset(index.Plain, descriptorStart);
        Assert.Equal(2, slots.Children.Count);
        // Use the first element's span, rather than assuming a fixed tag-header length.
        var descriptorPayload = slots.Children[0].Tag.Offset;
        for (var end = descriptorPayload; end < slots.Payload.End; end++)
        {
            var plain = index.Plain.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(plain.AsSpan(SizeOffset(plain, container.Tag.Offset)), end - container.Payload.Offset);
            BinaryPrimitives.WriteInt32LittleEndian(plain.AsSpan(SizeOffset(plain, slots.Tag.Offset)), end - slots.Payload.Offset);
            BinaryPrimitives.WriteInt32LittleEndian(plain.AsSpan(descriptorSizeOffset), end - descriptorPayload);
            var cursor = new SaveCursor(plain, container.Tag.Offset, end);
            var error = Assert.Throws<SaveFormatException>(() => new PropertyReader(plain).ReadSequence(cursor));
            Assert.True(error.Message.StartsWith("Truncated SaveExtension data at", StringComparison.Ordinal) ||
                error.Message == "Invalid SaveExtension count or length.", $"Cut at 0x{end:X}: {error.Message}");
        }
    }

    [Fact]
    public void EveryOuterBodyTruncationRejectsEvenWithARecomputedTrailer()
    {
        var bytes = SlotFixture.Create();
        // The existing outer truncation test deliberately leaves the digest invalid.
        for (var length = 0; length < bytes.Length - SaveExtensionFile.DigestBytes; length++)
        {
            var body = bytes[..length];
            byte[] truncated = [.. body, .. SlotFixture.Digest(body)];
            var error = Assert.Throws<SaveFormatException>(() => Open(truncated));
            Assert.DoesNotContain("SHA-1", error.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(32769)]
    [InlineData(int.MaxValue)]
    public void InventoryCollectionCountsRejectBeforeEnumeratingElements(int count)
    {
        var bytes = InventoryFixture.Create();
        var index = SlotDataDecoder.Decode(bytes, SaveExtensionFile.Parse(bytes));
        var component = Assert.Single(index.Objects, record => record.Kind == RecordKinds.Component);
        var slots = Assert.Single(Assert.Single(component.Properties).Children);
        BinaryPrimitives.WriteInt32LittleEndian(index.Plain.AsSpan(slots.Payload.Offset), count);
        var error = Assert.Throws<SaveFormatException>(() => Open(SlotFixture.Create(plain: index.Plain)));
        Assert.Equal("Invalid SaveExtension count or length.", error.Message);
    }

    [Theory]
    [InlineData(0, 2, "Invalid actor flag.")]
    [InlineData(1, 255, "Invalid actor flag.")]
    [InlineData(42, 2, "Invalid actor velocity flag.")]
    [InlineData(46, int.MaxValue, "Invalid SaveExtension count or length.")]
    public void ActorSuffixRejectsInvalidFlagsAndComponentCounts(int relativeOffset, int value, string message)
    {
        var bytes = InventoryFixture.Create();
        var index = SlotDataDecoder.Decode(bytes, SaveExtensionFile.Parse(bytes));
        var actor = Assert.Single(index.Objects, record => record.Kind == RecordKinds.Actor);
        var suffix = actor.Payload.End + sizeof(int); // Empty actor tag array.
        if (relativeOffset < 2) index.Plain[suffix + relativeOffset] = (byte)value;
        else BinaryPrimitives.WriteInt32LittleEndian(index.Plain.AsSpan(suffix + relativeOffset), value);
        var error = Assert.Throws<SaveFormatException>(() => Open(SlotFixture.Create(plain: index.Plain)));
        Assert.Equal(message, error.Message);
    }

    [Theory]
    [InlineData(16, true)]
    [InlineData(17, false)]
    public void InspectedStructDepthHasAnExplicitBoundary(int depth, bool accepted)
    {
        var bytes = NestedStructs(depth);
        var cursor = new SaveCursor(bytes);
        var parser = new PropertyReader(bytes);
        if (!accepted)
        {
            var error = Assert.Throws<SaveFormatException>(() => parser.ReadSequence(cursor));
            Assert.Equal("Tagged properties exceed the depth limit.", error.Message);
            return;
        }
        var members = parser.ReadSequence(cursor);
        Assert.Equal(0, cursor.Remaining);
        for (var index = 0; index < depth; index++) members = Assert.Single(members).Children;
        Assert.Empty(members);
    }

    [Fact]
    public void RealNestedPrimitiveCorruptionRejectsWithValidFramingAndHash()
    {
        var bytes = WriteTestData.Sample().ReadAllBytes();
        var outer = SaveExtensionFile.Parse(bytes);
        var index = SlotDataDecoder.Decode(bytes, outer);
        var state = new InventoryState(index, null);
        var items = state.Items.Where(item => item.Quantity.Type == "IntProperty").Take(3).ToArray();
        Assert.Equal(3, items.Length);
        foreach (var item in items)
        {
            var plain = index.Plain.ToArray();
            var tag = new SaveCursor(plain, item.Quantity.Tag.Offset, item.Quantity.Tag.End);
            _ = tag.String(); _ = tag.String();
            BinaryPrimitives.WriteInt32LittleEndian(plain.AsSpan(tag.Position), sizeof(int) - 1);
            var corrupt = SlotDataWriter.Write(bytes, outer, index.Plain, plain);
            _ = SaveExtensionFile.Parse(corrupt); // The corruption must reach the inner grammar.
            var error = Assert.Throws<SaveFormatException>(() => Open(corrupt));
            Assert.Contains("IntProperty payload size", error.Message, StringComparison.Ordinal);
        }
    }

    private static ISaveDocument Open(byte[] bytes) => new AtomicHeartPlugin().Open(MemorySaveSet.SingleFile("slot", bytes));

    private static int SizeOffset(byte[] plain, int tagStart)
    {
        var cursor = new SaveCursor(plain, tagStart);
        _ = cursor.String(); _ = cursor.String();
        return cursor.Position;
    }

    private static byte[] NestedStructs(int depth)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        if (depth > 0)
        {
            var payload = NestedStructs(depth - 1);
            SlotFixture.String(writer, "Nested"); SlotFixture.String(writer, "StructProperty");
            writer.Write(payload.Length); writer.Write(0);
            SlotFixture.String(writer, "InventoryList"); writer.Write(new byte[16]); writer.Write((byte)0);
            writer.Write(payload);
        }
        PlainFixture.End(writer);
        return stream.ToArray();
    }
}
