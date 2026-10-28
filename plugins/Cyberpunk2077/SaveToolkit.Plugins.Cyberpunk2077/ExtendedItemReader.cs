using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed record ItemAdditional(ulong LootPool, uint Unknown, float RequiredLevel);
internal sealed record ItemPart(ItemIdentity Identity, string Appearance, ulong AttachmentSlot, uint Unknown,
    ItemAdditional Additional, IReadOnlyList<ItemPart> Children);
internal sealed record ExtendedItem(byte Flags, uint CreationTime, ItemAdditional? Additional, ItemPart? Root);

/// <summary>Independent read-only implementation of the researched InventoryHelper wire schema.</summary>
internal static class ExtendedItemReader
{
    public const string Source = "Inventory wire-format research, WolvenKit 65ea74b (GPL-3.0; independently implemented): " +
        "https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Save/Helper/InventoryHelper.cs; " +
        "https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Save/Classes/ItemData.cs";
    private const int MaximumDepth = 12;
    private const int MaximumParts = 512;
    private const int HeaderLength = 25;

    public static IEnumerable<(string Path, ItemPart Part)> Parts(ItemPart root, string path = "root")
    {
        yield return (path, root);
        for (var i = 0; i < root.Children.Count; i++)
            foreach (var child in Parts(root.Children[i], path + "/" + i)) yield return child;
    }

    public static ExtendedItem Read(ReadOnlySpan<byte> bytes)
    {
        BinaryData.Slice(bytes, 0, HeaderLength);
        var structure = bytes[16];
        if (structure > 3) throw new SaveFormatException("Unsupported extended item structure.");
        var seed = BinaryData.UInt32(bytes, 12);
        var position = HeaderLength;
        if ((structure & 2) != 0 || structure == 0 && seed == 2) position += 4;
        BinaryData.Slice(bytes, 0, position);
        ItemAdditional? additional = null;
        ItemPart? root = null;
        if ((structure & 1) != 0 || structure == 0 && seed != 2)
        {
            additional = Additional(bytes, ref position);
            var remaining = MaximumParts;
            root = Part(bytes, ref position, 0, ref remaining);
        }
        if (position != bytes.Length) throw new SaveFormatException("Unexplained extended item suffix; raw bytes retained.");
        return new ExtendedItem(bytes[20], BinaryData.UInt32(bytes, 21), additional, root);
    }

    private static ItemAdditional Additional(ReadOnlySpan<byte> bytes, ref int position)
    {
        var raw = BinaryData.Slice(bytes, position, 16);
        var result = new ItemAdditional(BinaryData.UInt64(raw, 0), BinaryData.UInt32(raw, 8), BinaryPrimitives.ReadSingleLittleEndian(raw[12..]));
        if (!float.IsFinite(result.RequiredLevel)) throw new SaveFormatException("Nonfinite extended item level value.");
        position += 16;
        return result;
    }

    private static ItemPart Part(ReadOnlySpan<byte> bytes, ref int position, int depth, ref int remaining)
    {
        if (depth > MaximumDepth || --remaining < 0) throw new SaveFormatException("Extended item part limit exceeded.");
        var identity = ItemIdentity.Read(BinaryData.Slice(bytes, position, ItemIdentity.WireSize));
        position += ItemIdentity.WireSize;
        var appearance = ReadString(bytes, ref position);
        var slot = BinaryData.UInt64(bytes, position);
        position += 8;
        var count = BinaryData.PackedInt(bytes, ref position);
        if (count < 0 || count > remaining) throw new SaveFormatException("Invalid extended item child count.");
        var children = new List<ItemPart>();
        for (var i = 0; i < count; i++) children.Add(Part(bytes, ref position, depth + 1, ref remaining));
        var unknown = BinaryData.UInt32(bytes, position);
        position += 4;
        return new ItemPart(identity, appearance, slot, unknown, Additional(bytes, ref position), children);
    }

    internal static string ReadString(ReadOnlySpan<byte> bytes, ref int position)
    {
        const int maximumCharacters = 1024;
        var count = BinaryData.PackedInt(bytes, ref position);
        if (count < -maximumCharacters || count > maximumCharacters) throw new SaveFormatException("Inspection string exceeds limit.");
        var length = count > 0 ? count * 2 : -count;
        var raw = BinaryData.Slice(bytes, position, length);
        position += length;
        Encoding encoding = count > 0 ? new UnicodeEncoding(false, false, true) : new UTF8Encoding(false, true);
        try { return encoding.GetString(raw); }
        catch (DecoderFallbackException) { throw new SaveFormatException("Invalid inspection string encoding."); }
    }
}
