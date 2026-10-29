using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed record InventoryItem(int NodeId, ulong ContainerId, ulong TweakId, int? QuantityOffset, byte Structure);

internal static class InventoryReader
{
    public const ulong MoneyTweakId = 0x0000000BF5E188EC; // CRC32("Items.money") | UTF-8 length << 32.
    private const int ItemInfoSize = 16;
    private const int ItemPrefixSize = 4 + ItemInfoSize + 1 + 4;
    private const int VerifiedSaveVersion = 265;
    private const int VerifiedGameVersion = 2210;
    private const int VerifiedArchiveVersion = 195;

    public static bool IsVerifiedVersion(CsavContainer container) => container.SaveVersion == VerifiedSaveVersion &&
        container.GameVersion == VerifiedGameVersion && container.ArchiveVersion == VerifiedArchiveVersion;

    public static IReadOnlyList<InventoryItem> Read(CsavContainer container, CsavTree tree)
    {
        // Only this schema has been checked against the supplied PS5 bytes.
        if (!IsVerifiedVersion(container))
        {
            return [];
        }

        var result = new List<InventoryItem>();
        foreach (var inventory in tree.Nodes.Where(n => n.Name == "inventory"))
        {
            ReadInventory(container, tree, inventory, result);
        }

        return result;
    }

    private static void ReadInventory(CsavContainer container, CsavTree tree, CsavNode inventory, List<InventoryItem> result)
    {
        var raw = BinaryData.Slice(container.Payload, inventory.Offset, inventory.Size);
        var count = BinaryData.Int32(raw, sizeof(int));
        if (count < 0 || count > (raw.Length - 8) / 12)
        {
            throw new SaveFormatException("Invalid Cyberpunk sub-inventory count.");
        }

        var position = 8;
        var childIndex = 0;
        for (var index = 0; index < count; index++)
        {
            var containerId = BinaryData.UInt64(raw, position);
            var itemCount = BinaryData.Int32(raw, position + 8);
            position += 12;
            if (itemCount < 0 || itemCount > inventory.Children.Count - childIndex)
            {
                throw new SaveFormatException("Inventory item count disagrees with its node children.");
            }

            for (var itemIndex = 0; itemIndex < itemCount; itemIndex++)
            {
                var node = tree.Nodes[inventory.Children[childIndex++]];
                result.Add(ReadItem(raw, node, containerId, inventory.Offset, position));
                position = node.Offset - inventory.Offset + node.Size;
            }
        }

        if (childIndex != inventory.Children.Count || position != raw.Length)
        {
            throw new SaveFormatException("Cyberpunk inventory contains unexplained items or trailing data.");
        }
    }

    private static InventoryItem ReadItem(ReadOnlySpan<byte> raw, CsavNode node, ulong containerId, int inventoryOffset, int position)
    {
        var itemOffset = node.Offset - inventoryOffset;
        if (node.Name != "itemData" || node.Size < ItemPrefixSize || itemOffset != position + ItemInfoSize ||
            !BinaryData.Slice(raw, position, ItemInfoSize).SequenceEqual(BinaryData.Slice(raw, itemOffset + 4, ItemInfoSize)))
        {
            throw new SaveFormatException("Cyberpunk inventory item header disagrees with its itemData node.");
        }

        var tweakId = BinaryData.UInt64(raw, itemOffset + 4);
        var seed = BinaryData.UInt32(raw, itemOffset + 12);
        var structure = raw[itemOffset + 16];
        if (structure > 3)
        {
            throw new SaveFormatException("Unsupported Cyberpunk item structure.");
        }

        var hasQuantity = (structure & 2) != 0 || structure == 0 && seed == 2;
        int? quantity = hasQuantity ? node.Offset + ItemPrefixSize : null;
        if (hasQuantity)
        {
            if (node.Size < ItemPrefixSize + sizeof(uint))
            {
                throw new SaveFormatException("Cyberpunk itemData quantity is truncated.");
            }

            _ = BinaryData.Slice(raw, itemOffset + ItemPrefixSize, sizeof(uint));
        }

        return new InventoryItem(node.Id, containerId, tweakId, quantity, structure);
    }
}
