using System.Buffers.Binary;
using K4os.Compression.LZ4;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherWriteTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SyntheticPlayerEditsRelocateTablesPreserveUnknownsAndUndoExactly(bool storedPoints)
    {
        var input = SyntheticPlayerSave.Create(storedPoints);
        using var document = Open(input);
        Assert.True(document.CanEdit);
        _ = document.BuildView();
        Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Equal(storedPoints ? 3 : 0, document.GetValue("player/abilityPoints").AsInt32());
        ValueEdit[] edits = [new("player/level", FieldValue.Of(2)), new("player/xp", FieldValue.Of(20)),
            new("player/abilityPoints", FieldValue.Of(7)), new("inventory/1/quantity", FieldValue.Of(500))];
        var outcome = document.ApplyEdits(edits);
        Assert.True(outcome.Succeeded);
        var output = Assert.Single(document.Serialize()).Content.ToArray();
        using var reopened = Open(output);
        _ = reopened.BuildView();
        foreach (var edit in edits)
        {
            Assert.Equal(edit.Value, reopened.GetValue(edit.ValueId));
        }

        Assert.Equal(storedPoints ? 1000 : 0, reopened.GetValue("player/usedXp").AsInt32());
        Assert.Equal(storedPoints ? 2 : 0, reopened.GetValue("player/usedAbilityPoints").AsInt32());
        var before = WitcherContainer.Parse(input);
        var after = WitcherContainer.Parse(output);
        var oldDirectory = Sav3Directory.Parse(before);
        var newDirectory = Sav3Directory.Parse(after);
        Assert.Equal(oldDirectory.Names, newDirectory.Names.Take(oldDirectory.Names.Count));
        Assert.Equal(storedPoints ? oldDirectory.Names.Count : oldDirectory.Names.Count + 1, newDirectory.Names.Count);
        Assert.Equal(storedPoints ? before.Payload.Length : before.Payload.Length + 29, after.Payload.Length);
        Assert.Equal(new byte[] { 0x71, 0xFF, 0, 0xA2 }, UnknownProperty(after));
        Assert.Equal(before.Original.AsSpan(40, before.HeaderSize - 40).ToArray(), after.Original.AsSpan(40, after.HeaderSize - 40).ToArray());
        Assert.Equal(new byte[] { 0x55, 0xAA }, output[^2..]);
        Assert.Equal(newDirectory.Entries[^1].Offset + before.HeaderSize,
            BinaryData.Int32(after.Payload, newDirectory.VariableTableOffset + 4)); // Stored order remains reversed.
        Assert.True(document.ApplyEdits(outcome.Changes.Select(c => new ValueEdit(c.ValueId, c.OldValue)).ToArray()).Succeeded);
        Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void AtomicBatchWithReadonlyFieldLeavesNoEdits()
    {
        var input = SyntheticPlayerSave.Create();
        using var document = Open(input);
        _ = document.BuildView();
        Assert.False(document.ApplyEdits([new("inventory/1/quantity", FieldValue.Of(500)), new("header/0", FieldValue.Of(1))]).Succeeded);
        Assert.Equal(250, document.GetValue("inventory/1/quantity").AsInt32());
        Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void UnsupportedSchemaAndUnmatchedPlayerIdentityStayReadonly()
    {
        var container = WitcherContainer.Parse(SyntheticPlayerSave.Create());
        var data = container.Payload.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 24);
        using var newer = Open(SyntheticSave.Wrap(data));
        Assert.False(newer.CanEdit);
        var directory = Sav3Directory.Parse(container);
        var tree = Sav3Tree.Parse(container.Payload, directory);
        var archiveVersion = Assert.Single(tree.Nodes, n => n.Tag == "SXAP");
        var incompatibleArchive = container.Payload.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(incompatibleArchive.AsSpan(archiveVersion.Offset + 8), 24);
        using var differentInnerVersion = Open(SyntheticSave.Wrap(incompatibleArchive));
        Assert.False(differentInnerVersion.CanEdit);
        var identity = Assert.Single(tree.Nodes, n => n.Name == "id" && n.Type == "IdTag");
        container.Payload[identity.ValueOffset + 1] = 0x42;
        var mismatchedBytes = SyntheticSave.Wrap(container.Payload);
        using var mismatched = Open(mismatchedBytes);
        Assert.False(mismatched.CanEdit);
        Assert.Equal(mismatchedBytes, Assert.Single(mismatched.Serialize()).Content.ToArray());
    }

    [Fact]
    public void SetterFailureRollsBackEarlierEditsAndDocumentsAreIsolated()
    {
        var bytes = SyntheticPlayerSave.Create();
        using var first = Open(bytes);
        using var second = Open(bytes);
        _ = first.BuildView();
        _ = second.BuildView();
        var rejected = first.ApplyEdits([new("player/level", FieldValue.Of(2)), new("inventory/1/quantity", FieldValue.Of(65536))]);
        Assert.False(rejected.Succeeded);
        Assert.Equal(bytes, Assert.Single(first.Serialize()).Content.ToArray());
        Assert.True(first.ApplyEdits([new("player/abilityPoints", FieldValue.Of(7))]).Succeeded);
        Assert.Equal(0, second.GetValue("player/abilityPoints").AsInt32());
        Assert.Equal(bytes, Assert.Single(second.Serialize()).Content.ToArray());
    }

    [Fact]
    public void ChangedFirstChunkRecomputesDescriptorAndPreservesUnchangedSecondBlock()
    {
        var before = WitcherContainer.Parse(SyntheticSave.Create());
        var directory = Sav3Directory.Parse(before);
        var tree = Sav3Tree.Parse(before.Payload, directory);
        var node = Assert.Single(tree.Nodes, n => n.Offset == SyntheticSave.ScalarOffset);
        var replacement = before.Payload.AsSpan(node.Offset, node.Size).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(replacement.AsSpan(6), 0xAABBCCDD);
        var body = Sav3Writer.Write(before, directory, tree, [new Sav3Patch(node.Offset, node.Size, replacement)]);
        var output = WitcherWriter.Write(before, body);
        var after = WitcherContainer.Parse(output);
        Assert.Equal(body, after.Payload);
        Assert.Equal(before.Chunks.Select(c => c.ExpandedSize), after.Chunks.Select(c => c.ExpandedSize));
        Assert.Equal(after.HeaderSize + after.Chunks[0].StoredSize, after.Chunks[0].EndOffset);
        Assert.Equal(0, after.Chunks[1].EndOffset);
        Assert.Equal(before.Original.AsSpan(before.Chunks[1].Offset, before.Chunks[1].StoredSize).ToArray(),
            output.AsSpan(after.Chunks[1].Offset, after.Chunks[1].StoredSize).ToArray());
        Assert.Equal(before.Payload.AsSpan(node.Offset + node.Size).ToArray(), body.AsSpan(node.Offset + node.Size).ToArray());
        foreach (var chunk in after.Chunks)
        {
            Assert.Equal(chunk.StoredSize, BinaryData.Int32(output, 16 + after.Chunks.ToList().IndexOf(chunk) * 12));
        }
    }

    [Fact]
    public void WriterCanEmitAndReadRawIncompressibleBlock()
    {
        var input = SyntheticSave.Create();
        var parsed = WitcherContainer.Parse(input);
        // A synthetic 16-byte chunk consists of the validated SAV3 version header.
        var firstRaw = parsed.Payload[..16];
        var rest = parsed.Payload[16..];
        var compressed = new byte[LZ4Codec.MaximumOutputSize(rest.Length)];
        var size = LZ4Codec.Encode(rest, compressed);
        var original = new byte[parsed.HeaderSize + firstRaw.Length + size];
        input.AsSpan(0, parsed.HeaderSize).CopyTo(original);
        firstRaw.CopyTo(original, parsed.HeaderSize);
        compressed.AsSpan(0, size).CopyTo(original.AsSpan(parsed.HeaderSize + firstRaw.Length));
        Put(16, 16); Put(20, 16); Put(24, parsed.HeaderSize + 16);
        Put(28, size); Put(32, rest.Length); Put(36, 0);
        var rawContainer = WitcherContainer.Parse(original);
        Assert.Equal(parsed.Payload, rawContainer.Payload);
        var body = parsed.Payload.ToArray();
        PutBody(4, 65);
        var output = WitcherWriter.Write(rawContainer, body);
        var check = WitcherContainer.Parse(output);
        Assert.Equal(16, check.Chunks[0].StoredSize);
        Assert.Equal(body, check.Payload);

        void Put(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(original.AsSpan(offset), value);
        void PutBody(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(offset), value);
    }

    [Fact]
    public void GrowthAddsNativeSizedChunksOnlyWhenHeaderCapacityIsUnused()
    {
        var original = SyntheticSave.Create();
        original.AsSpan(40, SyntheticSave.HeaderSize - 40).Clear();
        var container = WitcherContainer.Parse(original);
        var directory = Sav3Directory.Parse(container);
        var tree = Sav3Tree.Parse(container.Payload, directory);
        var scalar = Assert.Single(tree.Nodes, n => n.Offset == SyntheticSave.ScalarOffset);
        const int nativeChunkSize = 1024 * 1024;
        var replacement = new byte[scalar.Size + nativeChunkSize];
        container.Payload.AsSpan(scalar.Offset, scalar.Size).CopyTo(replacement);
        replacement.AsSpan(scalar.Size).Fill(0x71);
        var payload = Sav3Writer.Write(container, directory, tree, [new Sav3Patch(scalar.Offset, scalar.Size, replacement)]);
        var bytes = WitcherWriter.Write(container, payload);
        var check = WitcherContainer.Parse(bytes);
        Assert.Equal(3, check.Chunks.Count);
        Assert.Equal(container.Chunks[0].ExpandedSize, check.Chunks[0].ExpandedSize);
        Assert.Equal(nativeChunkSize, check.Chunks[1].ExpandedSize);
        Assert.Equal(0, check.Chunks[2].EndOffset);
        Assert.All(check.Chunks.Take(2), c => Assert.Equal(c.Offset + c.StoredSize, c.EndOffset));
        Assert.Equal(payload, check.Payload);
        var reserved = WitcherContainer.Parse(SyntheticSave.Create());
        Assert.Throws<SaveFormatException>(() => WitcherWriter.Write(reserved, payload));
    }

    [Fact]
    public void RecompressionIsDeterministicAndRejectsInvalidBodyOrCrossingEdits()
    {
        var container = WitcherContainer.Parse(SyntheticSave.Create());
        Assert.Equal(container.Original, WitcherWriter.Write(container, container.Payload));
        var bad = container.Payload.ToArray();
        bad[^1] = 0;
        Assert.Throws<SaveFormatException>(() => WitcherWriter.Write(container, bad));
        var directory = Sav3Directory.Parse(container);
        var tree = Sav3Tree.Parse(container.Payload, directory);
        Assert.Throws<SaveFormatException>(() => Sav3Writer.Write(container, directory, tree,
            [new Sav3Patch(SyntheticSave.ScalarOffset - 1, 10, new byte[11])]));
    }

    [Theory]
    [InlineData("manager-length")]
    [InlineData("property-length")]
    [InlineData("point-count")]
    [InlineData("inventory-count")]
    [InlineData("inventory-name")]
    [InlineData("inventory-quantity")]
    [InlineData("inventory-terminal")]
    public void CorruptPlayerStructuresAreRejected(string kind)
    {
        var parsed = WitcherContainer.Parse(SyntheticPlayerSave.Create());
        var directory = Sav3Directory.Parse(parsed);
        var tree = Sav3Tree.Parse(parsed.Payload, directory);
        var manager = Assert.Single(tree.Nodes, n => n.Name == "levelManager");
        var player = PlayerData.Read(parsed, directory, tree)!;
        var data = parsed.Payload;
        var position = manager.ValueOffset + 8;
        var record = TaggedRecord.Read(data, ref position, manager.Offset + manager.Size, directory.Names);
        var crowns = player.Inventory!.Items[0];
        var mutation = kind switch {
            "manager-length" => manager.Offset + 8,
            "property-length" => record.Properties[0].Offset + 4,
            "point-count" => record.Find("points")!.Offset + 8,
            "inventory-count" => crowns.QuantityOffset - 17 - 2,
            "inventory-name" => crowns.QuantityOffset - 17,
            "inventory-quantity" => crowns.QuantityOffset,
            _ => crowns.QuantityOffset - 17 + 56,
        };
        if (kind is "inventory-quantity" or "inventory-name")
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(mutation), 0);
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(mutation), int.MaxValue);
        }

        Assert.Throws<SaveFormatException>(() => Open(SyntheticSave.Wrap(data)));
    }

    private static byte[] UnknownProperty(WitcherContainer container)
    {
        var directory = Sav3Directory.Parse(container);
        var tree = Sav3Tree.Parse(container.Payload, directory);
        var node = Assert.Single(tree.Nodes, n => n.Name == "levelManager");
        var position = node.ValueOffset + 8;
        var manager = TaggedRecord.Read(container.Payload, ref position, node.Offset + node.Size, directory.Names);
        return manager.Find("unknownProperty")!.Value.ToArray();
    }

    private static ISaveDocument Open(byte[] bytes) => new Witcher3Plugin().Open(MemorySaveSet.SingleFile("Manual.sav", bytes, "PPSA04018"));
}
