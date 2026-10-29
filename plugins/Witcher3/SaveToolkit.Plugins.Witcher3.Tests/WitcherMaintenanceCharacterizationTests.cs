using System.Buffers.Binary;
using System.Security.Cryptography;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherMaintenanceCharacterizationTests
{
    [Theory]
    [InlineData(false, "DBBD03F702ACCAB797E3346D8BD03A37247FFD8C1302EF45F21E814F197EDE33")]
    [InlineData(true, "AC87FD11586E854EB62DD613355C10D9F52FD423F4B8F91376295A456197BEDA")]
    public void ProgressionAndInventoryEditsKeepTheirExactEncodedOutput(bool storedPoints, string expectedHash)
    {
        var input = SyntheticPlayerSave.Create(storedPoints);
        using var document = WitcherTestFiles.Open(input);
        _ = document.BuildView();
        Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());
        var outcome = document.ApplyEdits([
            new("player/level", FieldValue.Of(2)), new("player/xp", FieldValue.Of(20)),
            new("player/abilityPoints", FieldValue.Of(7)), new("inventory/1/quantity", FieldValue.Of(500)),
        ]);
        Assert.True(outcome.Succeeded);
        var output = Assert.Single(document.Serialize()).Content.ToArray();
        // Captured from the pre-refactor writer: covers compressed framing as well as the expanded body.
        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(output)));
        Assert.True(document.ApplyEdits(outcome.Changes.Select(c => new ValueEdit(c.ValueId, c.OldValue)).ToArray()).Succeeded);
        Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InventoryReaderKeepsAllRecordMetadataAndAbsoluteOffsets(bool equipped)
    {
        var (node, bytes, names) = InventoryFixture(equipped);
        var item = Assert.Single(Assert.IsType<PlayerInventory>(PlayerInventory.Read(node, bytes, names)).Items);
        Assert.Equal(("Bread", 7, (ushort)9, 35, 37), (item.Name, item.Id, item.Quantity, item.QuantityOffset, item.DurabilityOffset));
        Assert.Equal(equipped, item.Equipped);
        Assert.Equal(equipped ? 0x200000U : 0U, item.Flags);
        Assert.Equal(equipped ? "slot" : "", item.Slot);
        Assert.Equal("tag", Assert.Single(item.Tags));
        Assert.Collection(item.PrefixNames, name => Assert.Equal("prefix", name), name => Assert.Equal("", name));
        Assert.Equal(new InventoryAttribute("attribute", 0xFEDCBA98, 2), Assert.Single(item.Attributes));
        Assert.Equal("01020304050607", item.OpaquePrefix);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3, 4, 5, 6, 7 })), item.OpaqueHash);
        Assert.Equal((18, equipped ? 39 : 37), (item.Offset, item.Size));
        Assert.Equal(42.5f, item.Durability);
        Assert.Equal(item.Durability, item.OriginalDurability);
        Assert.Equal(item.Quantity, item.OriginalQuantity);
    }

    [Theory]
    [InlineData(8, "Invalid player inventory name index.")]
    [InlineData(11, "Invalid player inventory item count.")]
    [InlineData(44, "Invalid player inventory item ID or tag count.")]
    [InlineData(48, "Invalid player inventory name index.")]
    public void InventoryReaderKeepsValidationMessages(int relativeOffset, string expected)
    {
        var (node, bytes, names) = InventoryFixture(false);
        bytes[node.Offset + relativeOffset] = 0xFF;
        Assert.Equal(expected, Assert.Throws<SaveFormatException>(() => PlayerInventory.Read(node, bytes, names)).Message);
    }

    [Theory]
    [InlineData("handle", "Unsupported player level manager handle.")]
    [InlineData("definitions", "Invalid player level definition count.")]
    [InlineData("points", "Unsupported player spendable points array.")]
    public void ProgressionReaderKeepsValidationMessages(string target, string expected)
    {
        var parsed = EnabledWriteFixture.Read(SyntheticPlayerSave.Create());
        var node = parsed.Tree.Nodes.Single(n => n.Name == "levelManager");
        var bytes = parsed.Container.Payload.ToArray();
        var position = node.ValueOffset + 8;
        var manager = TaggedRecord.Read(bytes, ref position, node.Offset + node.Size, parsed.Directory.Names);
        var offset = target switch {
            "definitions" => manager.Find("levelDefinitions")!.Offset + 8,
            "points" => manager.Find("points")!.Offset + 8,
            _ => node.ValueOffset,
        };
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), -1);
        Assert.Equal(expected, Assert.Throws<SaveFormatException>(() => new PlayerProgress(node, bytes, parsed.Directory.Names)).Message);
    }

    private static (Sav3Node Node, byte[] Bytes, string[] Names) InventoryFixture(bool equipped)
    {
        const int offset = 5;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[offset]);
        writer.Write(new byte[] { 0, 0, 1, 0xA2, 0x15, 0x35, 0xB6 });
        writer.Write((ushort)1); writer.Write((ushort)7); writer.Write((ushort)1);
        writer.Write((ushort)2); writer.Write(equipped ? 0x200000U : 0U);
        writer.Write(new byte[] { 1, 2, 3, 4, 5, 6, 7 });
        writer.Write((ushort)3); writer.Write((ushort)0); writer.Write((ushort)9);
        writer.Write(42.5f); writer.Write((byte)1);
        writer.Write((ushort)4); writer.Write(0xFEDCBA98U); writer.Write((byte)2);
        writer.Write(new byte[] { 7, 0, 0, 1 }); writer.Write((ushort)5);
        if (equipped) { writer.Write((ushort)6); }
        writer.Write(new byte[] { 1, 0, 0, 0, 0, 0 });
        var bytes = stream.ToArray();
        return (new Sav3Node(0, offset, bytes.Length - offset, "Opaque", "tail", null, offset, bytes.Length - offset),
            bytes, ["CInventoryComponent", "Bread", "prefix", "attribute", "tag", "slot"]);
    }
}
