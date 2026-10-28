using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Plugins.UnrealGvas.Gvas;

namespace SaveToolkit.Plugins.UnrealGvas;

/// <summary>Conservative fallback for custom actor structs the general GVAS parser cannot schema.</summary>
internal static class JediActorScanner
{
    private const int MaxDepth = 32;
    private const int MaxStringLength = GvasFile.MaxStringLength;

    public static IReadOnlyList<GvasNode> Scan(ReadOnlySpan<byte> bytes)
    {
        var nodes = ReadRange(bytes, 0, bytes.Length, 0);
        return nodes.Count == 0 ? ScanKnownNames(bytes) : nodes;
    }

    private static IReadOnlyList<GvasNode> ScanKnownNames(ReadOnlySpan<byte> bytes)
    {
        var nodes = new List<GvasNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var offset = 0; offset <= bytes.Length - 5 && nodes.Count < 2_000; offset++)
        {
            var length = BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
            if (length is <= 1 or > 256 || offset + 4 + length > bytes.Length || bytes[offset + 4 + length - 1] != 0) continue;
            var name = Encoding.UTF8.GetString(bytes.Slice(offset + 4, length - 1));
            if (name is "None" or "ArrayProperty" or "StructProperty" || !seen.Add(name)) continue;
            nodes.Add(new GvasNode(name, "ObservedFString", offset + 4, length)
            {
                Note = "Name observed in the bounded actor stream scanner; surrounding property schema is not asserted."
            });
        }
        return nodes;
    }

    private static List<GvasNode> ReadRange(ReadOnlySpan<byte> bytes, int start, int end, int depth)
    {
        var nodes = new List<GvasNode>();
        if (depth > MaxDepth) return nodes;
        var position = start;
        while (position < end && nodes.Count < GvasFile.MaxNodes)
        {
            if (!TryTag(bytes, position, end, out var tag)) break;
            if (tag.IsNone) break;
            if (tag.Next <= position || tag.PayloadEnd > end) break;
            var node = new GvasNode(tag.Name, tag.Type, tag.PayloadStart, tag.Size);
            if (tag.Scalar is not null) node.Scalar = tag.Scalar;
            else if (depth < MaxDepth && tag.Type is "StructProperty" or "ArrayProperty" or "SetProperty" or "MapProperty")
            {
                var childStart = tag.Type == "ArrayProperty" && tag.PayloadEnd - tag.PayloadStart >= 4
                    ? tag.PayloadStart + 4 : tag.PayloadStart;
                node.Children = ReadRange(bytes, childStart, tag.PayloadEnd, depth + 1);
                if (node.Children.Count == 0) node.Note = "Custom actor payload; child schema is incomplete and bytes remain opaque.";
            }
            else if (tag.Type is not "BoolProperty")
                node.Note = "Actor payload is preserved; no game-specific schema is asserted.";
            nodes.Add(node);
            position = tag.Next;
        }
        return nodes;
    }

    private static bool TryTag(ReadOnlySpan<byte> bytes, int start, int end, out Tag tag)
    {
        tag = default;
        var position = start;
        if (!TryString(bytes, ref position, end, out var name)) return false;
        if (name == "None")
        {
            tag = new Tag(name, "None", position, position, position, null, true);
            return true;
        }
        if (!TryString(bytes, ref position, end, out var type) || position > end - 8) return false;
        var size = BinaryPrimitives.ReadInt32LittleEndian(bytes[position..]);
        position += 8;
        if (size < 0 || size > end - position) return false;
        string? inner = null;
        var boolOffset = -1;
        var boolValue = false;
        switch (type)
        {
            case "BoolProperty":
                if (position >= end) return false;
                boolOffset = position;
                boolValue = bytes[position++] != 0;
                break;
            case "ArrayProperty": case "SetProperty": case "ByteProperty": case "EnumProperty":
                if (!TryString(bytes, ref position, end, out inner)) return false;
                break;
            case "MapProperty":
                if (!TryString(bytes, ref position, end, out inner) || !TryString(bytes, ref position, end, out _)) return false;
                break;
            case "StructProperty":
                if (!TryString(bytes, ref position, end, out _) || position > end - 16) return false;
                position += 16;
                break;
        }
        if (position >= end) return false;
        var hasGuid = bytes[position++];
        if (hasGuid > 1 || (hasGuid != 0 && position > end - 16)) return false;
        if (hasGuid != 0) position += 16;
        var payloadStart = position;
        var payloadEnd = checked(payloadStart + size);
        if (payloadEnd > end) return false;
        var scalar = type == "BoolProperty"
            ? new GvasScalar(GvasScalarKind.Boolean, boolOffset, 1, boolValue)
            : ReadScalar(type, bytes, payloadStart, size);
        tag = new Tag(name, type, payloadStart, payloadEnd, payloadEnd, scalar, false, inner);
        return true;
    }

    private static GvasScalar? ReadScalar(string type, ReadOnlySpan<byte> bytes, int offset, int size)
    {
        try
        {
            return type switch
            {
                "Int8Property" when size == 1 => new GvasScalar(GvasScalarKind.Signed, offset, 1, (Int128)(sbyte)bytes[offset]),
                "IntProperty" when size == 4 => new GvasScalar(GvasScalarKind.Signed, offset, 4, (Int128)BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..])),
                "UInt32Property" when size == 4 => new GvasScalar(GvasScalarKind.Unsigned, offset, 4, (Int128)BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..])),
                "Int64Property" when size == 8 => new GvasScalar(GvasScalarKind.Signed, offset, 8, (Int128)BinaryPrimitives.ReadInt64LittleEndian(bytes[offset..])),
                "FloatProperty" when size == 4 => new GvasScalar(GvasScalarKind.Float, offset, 4, (double)BinaryPrimitives.ReadSingleLittleEndian(bytes[offset..])),
                "DoubleProperty" when size == 8 => new GvasScalar(GvasScalarKind.Double, offset, 8, BinaryPrimitives.ReadDoubleLittleEndian(bytes[offset..])),
                "StrProperty" or "NameProperty" or "EnumProperty" when TryValueString(bytes, offset, size, out var value) => new GvasScalar(GvasScalarKind.String, offset, size, value),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException) { return null; }
    }

    private static bool TryValueString(ReadOnlySpan<byte> bytes, int offset, int size, out string value)
    {
        value = "";
        var position = offset;
        if (!TryString(bytes, ref position, offset + size, out value)) return false;
        return position <= offset + size;
    }

    private static bool TryString(ReadOnlySpan<byte> bytes, ref int position, int end, out string value)
    {
        value = "";
        if (position > end - 4) return false;
        var length = BinaryPrimitives.ReadInt32LittleEndian(bytes[position..]);
        position += 4;
        if (length <= 0 || length > MaxStringLength || position > end - length) return false;
        var raw = bytes.Slice(position, length);
        if (raw[^1] != 0) return false;
        value = Encoding.UTF8.GetString(raw[..^1]);
        position += length;
        return true;
    }

    private readonly record struct Tag(string Name, string Type, int PayloadStart, int PayloadEnd, int Next,
        GvasScalar? Scalar, bool IsNone, string? Inner = null)
    {
        public int Size => PayloadEnd - PayloadStart;
    }
}
