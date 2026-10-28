namespace SaveToolkit.Plugins.Pragmata.Format;

internal sealed class SaveNode(uint hash, int type, SaveNode? parent = null, int index = -1)
{
    public uint Hash { get; } = hash;
    public int Type { get; } = type;
    public SaveNode? Parent { get; } = parent;
    public int Index { get; } = index;
    public uint ClassHash { get; set; }
    public int Offset { get; set; }
    public int Size { get; set; }
    public IReadOnlyList<SaveNode> Children { get; set; } = [];
    public bool IsPrimitive => Type is >= FieldTags.Enum and <= FieldTags.Char16;
    public string Path => Parent is null ? $"{Hash:x8}" :
        Index >= 0 ? $"{Parent.Path}[{Index}]" : $"{Parent.Path}/{Hash:x8}";

    public SaveNode? Find(uint hash) => Children.FirstOrDefault(n => n.Hash == hash);
}
