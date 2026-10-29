using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Binary;

namespace SaveToolkit.Plugins.UnrealGvas.Gvas;

internal sealed record GvasTag(string Name, string Type, int SizeOffset, int Size,
    string? InnerType = null, string? ValueType = null, string? StructType = null,
    string? ElementStructType = null, int? BoolOffset = null, bool BoolValue = false, bool Native = false)
{
    private const byte HasArrayIndex = 1;
    private const byte HasPropertyGuid = 2;
    private const byte HasPropertyExtensions = 4;
    private const byte BoolTrue = 16;
    private const byte NativeSerialization = 8;
    private const byte KnownFlags = HasArrayIndex | HasPropertyGuid | HasPropertyExtensions | BoolTrue | NativeSerialization;

    public static GvasTag? Read(ref BoundedBinaryCursor reader, GvasHeader header)
    {
        var name = reader.String();
        if (name == "None") return null;
        if (string.IsNullOrEmpty(name)) throw new SaveFormatException("Empty GVAS property name.");
        if (header.CompleteTypeNames) return ReadComplete(ref reader, name);
        var type = reader.String();
        var offset = reader.Position;
        var size = reader.ReadCount(GvasFile.MaxFileLength);
        _ = reader.ReadUInt32LE(); // Static array index; preserved verbatim.
        string? inner = null, value = null, structure = null;
        int? boolOffset = null;
        var boolValue = false;
        switch (type)
        {
            case "BoolProperty":
                boolOffset = reader.Position;
                var boolean = reader.ReadByte();
                if (boolean > 1) throw new SaveFormatException("Invalid GVAS boolean.");
                boolValue = boolean != 0;
                break;
            case "ArrayProperty": case "SetProperty": case "ByteProperty": case "EnumProperty":
                inner = reader.String(); break;
            case "MapProperty": inner = reader.String(); value = reader.String(); break;
            case "StructProperty": structure = reader.String(); reader.Take(16); break;
        }
        if (header.HasPropertyGuid)
        {
            var hasGuid = reader.ReadByte();
            if (hasGuid > 1) throw new SaveFormatException("Invalid GVAS property GUID flag.");
            if (hasGuid != 0) reader.Take(16);
        }
        return new GvasTag(name, type, offset, size, inner, value, structure, BoolOffset: boolOffset, BoolValue: boolValue);
    }

    private static GvasTag ReadComplete(ref BoundedBinaryCursor reader, string name)
    {
        var root = ReadType(ref reader, 0);
        var offset = reader.Position;
        var size = reader.ReadCount(GvasFile.MaxFileLength);
        var flagOffset = reader.Position;
        var flags = reader.ReadByte();
        if ((flags & ~KnownFlags) != 0) throw new SaveFormatException("Unknown UE5 property tag flags.");
        if ((flags & HasArrayIndex) != 0) reader.Take(4);
        if ((flags & HasPropertyGuid) != 0) reader.Take(16);
        if ((flags & HasPropertyExtensions) != 0) throw new SaveFormatException("UE5 property tag extensions are not supported.");
        var structure = root.Name == "StructProperty" ? root.Inner.FirstOrDefault()?.Name : null;
        var inner = root.Inner.FirstOrDefault();
        return new GvasTag(name, root.Name, offset, size, inner?.Name,
            root.Inner.Skip(1).FirstOrDefault()?.Name, structure,
            inner?.Name == "StructProperty" ? inner.Inner.FirstOrDefault()?.Name : null,
            root.Name == "BoolProperty" ? flagOffset : null, (flags & BoolTrue) != 0, (flags & NativeSerialization) != 0);
    }

    private static TypeName ReadType(ref BoundedBinaryCursor reader, int depth)
    {
        if (depth > 16) throw new SaveFormatException("UE5 type name nesting exceeds the safety limit.");
        var name = reader.String();
        var count = reader.ReadCount(16);
        var children = new List<TypeName>();
        for (var i = 0; i < count; i++) children.Add(ReadType(ref reader, depth + 1));
        return new TypeName(name, children);
    }

    private sealed record TypeName(string Name, IReadOnlyList<TypeName> Inner);
}
