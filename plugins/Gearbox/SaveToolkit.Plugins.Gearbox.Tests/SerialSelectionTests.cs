namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class SerialSelectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SerialSelection_PrefersLastNestedBytesThenDirectBytesWithoutCopying(bool wonderlands)
    {
        var schema = wonderlands ? GearboxSchema.Tina : GearboxSchema.Bl3;
        var itemType = schema.Spec("Character", "inventory_items").Type;
        var message = new ProtoMessage(schema, itemType, []);
        byte[] direct = [1, 2];
        byte[] first = [3, 4];
        byte[] last = [5, 6];
        var field = new ProtoField(1, 2, null, [], direct, message);
        Assert.Same(direct, InventoryEntry.SerialOf(field));

        var spec = schema.Spec(itemType, "item_serial_number");
        var tag = schema.Tag(itemType, "item_serial_number");
        message.Add(new ProtoField(tag, 2, spec, [], first, null));
        message.Add(new ProtoField(tag, 2, spec, [], last, null));
        Assert.Same(last, InventoryEntry.SerialOf(field));

        // The last record wins even when it is empty or has an unsupported value type.
        var final = new ProtoField(tag, 2, spec, [], Array.Empty<byte>(), null);
        message.Add(final);
        Assert.Empty(InventoryEntry.SerialOf(field));
        final.Set("unsupported");
        Assert.Same(direct, InventoryEntry.SerialOf(field));
        Assert.Empty(InventoryEntry.SerialOf(new ProtoField(1, 2, null, [], null, message)));
        Assert.Empty(InventoryEntry.SerialOf(new ProtoField(1, 2, null, [], null, null)));
        Assert.Same(direct, InventoryEntry.SerialOf(new ProtoField(1, 2, null, [], direct, null)));
        Assert.Equal(new byte[] { 1, 2 }, direct);
        Assert.Equal(new byte[] { 3, 4 }, first);
        Assert.Equal(new byte[] { 5, 6 }, last);
    }
}
