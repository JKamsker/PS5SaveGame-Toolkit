using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AtomicHeart;

/// <summary>UE4 string-proxy property tags. Native structs and unrecognized collections stay opaque.</summary>
internal sealed class PropertyReader(byte[] bytes)
{
    private const int MaxDepth = 16;
    private const int MaxObjectProperties = 1024;
    private const int GuidBytes = 16;
    private int propertyCount;

    internal IReadOnlyList<TaggedProperty> ReadSequence(SaveCursor reader, int depth = 0)
    {
        if (depth > MaxDepth) throw new SaveFormatException("Tagged properties exceed the depth limit.");
        var properties = new List<TaggedProperty>();
        while (properties.Count < MaxObjectProperties)
        {
            var property = ReadTag(reader);
            if (property is null) return properties;
            properties.Add(property with { Children = ReadChildren(property, depth) });
        }
        throw new SaveFormatException("Object exceeds 1,024 tagged properties.");
    }

    private TaggedProperty? ReadTag(SaveCursor reader)
    {
        var start = reader.Position;
        var name = reader.String(RecordIndex.MaxNameUnits);
        if (name == "None") return null;
        if (name.Length == 0 || ++propertyCount > RecordIndex.MaxProperties)
            throw new SaveFormatException("Invalid or excessive tagged properties.");
        var type = reader.String(RecordIndex.MaxNameUnits);
        var size = reader.Count(reader.Remaining);
        var arrayIndex = reader.Int32();
        var detail = string.Empty;
        if (type == "StructProperty") { detail = reader.String(RecordIndex.MaxNameUnits); reader.Take(GuidBytes); }
        else if (type is "ArrayProperty" or "SetProperty" or "ByteProperty" or "EnumProperty")
            detail = reader.String(RecordIndex.MaxNameUnits);
        else if (type == "MapProperty")
            detail = reader.String(RecordIndex.MaxNameUnits) + "/" + reader.String(RecordIndex.MaxNameUnits);
        byte? boolean = type == "BoolProperty" ? reader.Byte() : null;
        if (boolean > 1) throw new SaveFormatException("Invalid property boolean.");
        var hasGuid = reader.Byte();
        if (hasGuid > 1) throw new SaveFormatException("Invalid property GUID flag.");
        if (hasGuid != 0) reader.Take(GuidBytes);
        PrimitivePayload.ValidateSize(type, detail, size);
        var payload = new ByteSpan(reader.Position, size);
        reader.Take(size);
        var value = ReadValue(type, payload, boolean);
        return new TaggedProperty(name, type, detail, arrayIndex, new(start, reader.Position - start), payload, value);
    }

    private FieldValue? ReadValue(string type, ByteSpan span, byte? boolean)
    {
        var reader = new SaveCursor(bytes, span.Offset, span.End);
        FieldValue? value = type switch
        {
            "BoolProperty" when span.Length == 0 => FieldValue.Of(boolean == 1),
            "IntProperty" when span.Length == sizeof(int) => FieldValue.Of(reader.Int32()),
            "Int64Property" when span.Length == sizeof(long) => FieldValue.Of(reader.Int64()),
            "UInt32Property" when span.Length == sizeof(uint) => FieldValue.Of((ulong)unchecked((uint)reader.Int32())),
            "UInt64Property" when span.Length == sizeof(ulong) => FieldValue.Of(reader.UInt64()),
            "FloatProperty" when span.Length == sizeof(float) => Finite(reader.Single()),
            "DoubleProperty" when span.Length == sizeof(double) => Finite(reader.Double()),
            "NameProperty" or "StrProperty" => FieldValue.Of(reader.String()),
            _ => null,
        };
        if (value is not null && reader.Remaining != 0 && type != "BoolProperty")
            throw new SaveFormatException("Invalid tagged primitive length.");
        // ObjectProperty can also contain native inline state (e.g. AttributeSet), not an object path.
        if (type == "ObjectProperty")
        {
            try { var text = reader.String(); if (reader.Remaining == 0) value = FieldValue.Of(text); }
            catch (SaveFormatException) { /* Native payload is retained, without guessing its grammar. */ }
        }
        return value;
    }

    private static FieldValue? Finite(double value) => double.IsFinite(value) ? FieldValue.Of(value) : null;

    private IReadOnlyList<TaggedProperty> ReadChildren(TaggedProperty property, int depth)
    {
        var reader = new SaveCursor(bytes, property.Payload.Offset, property.Payload.End);
        // These are observed tagged structs; native Transform, GameplayTagContainer, etc. stay raw.
        if (property.Type == "StructProperty" && property.Detail is
            "InventoryList" or "InventoryGrid" or "InventoryDump" or "InventoryData" or "SkillDump" or "PrimaryAssetId" or "PrimaryAssetType")
        {
            var children = ReadSequence(reader, depth + 1);
            RequireEnd(reader);
            return children;
        }
        if (property.Type == "ArrayProperty" && property.Detail == "StructProperty" &&
            property.Name is "Slots" or "AllCraftablePrimaryAssetIDs")
        {
            var count = reader.Count(RecordIndex.MaxCollection);
            var descriptor = ReadTag(reader) ?? throw new SaveFormatException("Missing struct array descriptor.");
            if (descriptor.Name != property.Name || descriptor.Type != "StructProperty" || descriptor.Payload.End != property.Payload.End)
                throw new SaveFormatException("Invalid struct array bounds.");
            if (descriptor.Detail != (property.Name == "Slots" ? "InventoryData" : "PrimaryAssetId")) return [];
            reader.Position = descriptor.Payload.Offset;
            return ReadElements(reader, count, depth, keyed: false);
        }
        if (property.Type == "MapProperty" && property.Detail == "Int64Property/StructProperty" && property.Name == "Elements")
        {
            var removed = reader.Count(RecordIndex.MaxCollection);
            reader.Take(checked(removed * sizeof(long)));
            var count = reader.Count(RecordIndex.MaxCollection);
            return ReadElements(reader, count, depth, keyed: true);
        }
        return [];
    }

    private IReadOnlyList<TaggedProperty> ReadElements(SaveCursor reader, int count, int depth, bool keyed)
    {
        var elements = new List<TaggedProperty>();
        for (var index = 0; index < count; index++)
        {
            var start = reader.Position;
            var key = keyed ? reader.Int64().ToString() : index.ToString();
            var children = ReadSequence(reader, depth + 1);
            var span = new ByteSpan(start, reader.Position - start);
            elements.Add(new TaggedProperty(key, "StructElement", "", 0, span, span, null) { Children = children });
        }
        RequireEnd(reader);
        return elements;
    }

    private static void RequireEnd(SaveCursor reader)
    {
        if (reader.Remaining != 0) throw new SaveFormatException("Unexpected bytes in an inspected tagged collection.");
    }
}
