namespace SaveToolkit.Plugins.BaldursGate3;

internal sealed class ResourceNode(string name, int parent)
{
    internal string Name { get; } = name;
    internal int Parent { get; } = parent;
    internal string? KeyAttribute { get; set; }
    internal List<int> Children { get; } = [];
    internal List<int> Attributes { get; } = [];
}

internal sealed record ResourceAttribute(string Name, LarianAttributeType Type, ReadOnlyMemory<byte> Data)
{
    internal string? XmlValue { get; init; }
}

internal sealed record LarianResource(string Format, IReadOnlyList<ResourceNode> Nodes,
    IReadOnlyList<ResourceAttribute> Attributes, IReadOnlyList<int> Roots);

// Numeric IDs and layouts from Norbyte/LSLib, MIT; see THIRD-PARTY-NOTICES.md.
internal enum LarianAttributeType
{
    None, Byte, Short, UShort, Int, UInt, Float, Double,
    IVec2, IVec3, IVec4, Vec2, Vec3, Vec4, Mat2, Mat3, Mat3x4, Mat4x3, Mat4,
    Bool, String, Path, FixedString, LSString, ULongLong, ScratchBuffer,
    Long, Int8, TranslatedString, WString, LSWString, UUID, Int64, TranslatedFSString,
}
