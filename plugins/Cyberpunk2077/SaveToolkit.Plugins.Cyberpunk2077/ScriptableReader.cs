using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077;

/// <summary>Bounded inspection of known nested RED structs; never used to serialize them.</summary>
internal sealed class ScriptableReader
{
    public const string SourceUrl = "https://github.com/WolvenKit/WolvenKit/tree/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Classes/";
    public const string Source = "RED4 schema research, WolvenKit 65ea74b014e11bb41659c65401e43d5ebe4d8d75 " +
        "(GPL-3.0, format reference only; no implementation copied). " +
        SourceUrl;
    private const int MaximumDepth = 12;
    private readonly byte[] _bytes;
    public RedPackage Package { get; }

    public ScriptableReader(CsavContainer container, CsavTree tree) : this(ReadPackage(container, tree)) { }

    internal ScriptableReader(byte[] bytes)
    {
        _bytes = bytes.ToArray();
        Package = new RedPackage(_bytes, true);
    }

    private static byte[] ReadPackage(CsavContainer container, CsavTree tree)
    {
        if (!InventoryReader.IsVerifiedVersion(container))
            throw new SaveFormatException("Read-only schema inspection requires PS5 save/game/archive 265/2210/195.");
        var nodes = tree.Nodes.Where(n => n.Name == "ScriptableSystemsContainer").ToArray();
        if (nodes.Length != 1) throw new SaveFormatException("No unique scriptable systems package.");
        return ProgressionData.PackageBytes(container.Payload, nodes[0]);
    }

    public PackageClass System(string type)
    {
        var indices = Package.Types.Select((name, index) => (name, index)).Where(p => p.name == type).ToArray();
        if (indices.Length != 1) throw new SaveFormatException($"No unique {type} record.");
        return Package.Chunk(indices[0].index);
    }

    public IReadOnlyList<PackageClass> Handles(PackageClass owner, string field, string targetType)
    {
        if (owner.Find(field) is null) return [];
        var raw = owner.Raw(owner.Require(field, "array:handle:" + targetType));
        var count = RedPackage.Count(raw, 0);
        if (raw.Length != 4 + count * 4) throw new SaveFormatException("Invalid scriptable handle array length.");
        var result = new List<PackageClass>();
        var seen = new HashSet<int>();
        for (var i = 0; i < count; i++)
        {
            var index = BinaryData.Int32(raw, 4 + i * 4);
            if (index < 0 || index >= Package.Types.Count || Package.Types[index] != targetType || !seen.Add(index))
                throw new SaveFormatException("Invalid, mistyped or duplicate scriptable handle.");
            result.Add(Package.Chunk(index));
        }
        return result;
    }

    public PackageClass Player(string system, string handles, string targetType)
    {
        var players = Handles(System(system), handles, targetType).Where(record =>
            UInt64(Nested(record, "ownerID", "entEntityID"), "hash", "Uint64") == 1).ToArray();
        return players.Length == 1 ? players[0] : throw new SaveFormatException("No unique owner-1 record.");
    }

    public PackageClass Nested(PackageClass owner, string name, string type)
    {
        var field = owner.Require(name, type);
        return new PackageClass(Package, _bytes, field.Offset, field.Offset + field.Length);
    }

    public IReadOnlyList<PackageClass> Array(PackageClass owner, string name, string type)
    {
        if (owner.Find(name) is null) return [];
        var field = owner.Require(name, "array:" + type);
        var count = RedPackage.Count(_bytes, field.Offset);
        var end = field.Offset + field.Length;
        var position = field.Offset + 4;
        var result = new List<PackageClass>();
        for (var i = 0; i < count; i++)
        {
            var size = StructSize(position, end, 0);
            result.Add(new PackageClass(Package, _bytes, position, position + size));
            position += size;
        }
        if (position != end) throw new SaveFormatException("Unexplained nested struct array bytes.");
        return result;
    }

    public static ulong UInt64(PackageClass owner, string name, string type) => owner.Find(name) is null ? 0
        : BinaryData.UInt64(Exact(owner, name, type, 8), 0);
    public static uint UInt32(PackageClass owner, string name) => owner.Find(name) is null ? 0
        : BinaryData.UInt32(Exact(owner, name, "Uint32", 4), 0);
    public static ushort UInt16(PackageClass owner, string name) => owner.Find(name) is null ? (ushort)0
        : RedPackage.UInt16(Exact(owner, name, "Uint16", 2), 0);
    public static byte Byte(PackageClass owner, string name, string type) => owner.Find(name) is null ? (byte)0
        : Exact(owner, name, type, 1)[0];
    private static byte[] Exact(PackageClass owner, string name, string type, int length)
    {
        var raw = owner.Raw(owner.Require(name, type));
        return raw.Length == length ? raw : throw new SaveFormatException($"Invalid {name} width.");
    }

    private int StructSize(int start, int end, int depth)
    {
        if (depth > MaximumDepth) throw new SaveFormatException("Nested inspection depth limit exceeded.");
        var record = new PackageClass(Package, _bytes, start, end);
        var position = start + 2 + record.Fields.Count * 8;
        foreach (var field in record.Fields)
        {
            var size = Size(field.Type, field.Offset, field.Offset + field.Length, depth + 1);
            if (size > field.Length) throw new SaveFormatException("Nested inspection members overlap.");
            position = Math.Max(position, field.Offset + size);
        }
        return position - start;
    }

    private int Size(string type, int start, int end, int depth)
    {
        if (depth > MaximumDepth) throw new SaveFormatException("Nested inspection depth limit exceeded.");
        if (type.StartsWith("array:", StringComparison.Ordinal))
        {
            var count = RedPackage.Count(_bytes, start);
            var position = start + 4;
            for (var i = 0; i < count; i++) position += Size(type[6..], position, end, depth + 1);
            if (position > end) throw new SaveFormatException("Nested array exceeds its parent.");
            return position - start;
        }
        var size = type switch
        {
            "Int32" or "Uint32" or "Float" => 4,
            "Uint64" or "TweakDBID" => 8,
            "Uint16" => 2,
            "Bool" or "Uint8" => 1,
            "SAttributeData" or "SNewPerk" or "SPerk" or "SPerkArea" or "gameItemID" or
                "gameSLoadout" or "gameSEquipArea" or "gameSEquipSlot" => StructSize(start, end, depth + 1),
            _ when type.StartsWith("gamedata", StringComparison.Ordinal) => 2,
            _ when type.StartsWith("handle:", StringComparison.Ordinal) => 4,
            _ => throw new SaveFormatException($"Unverified nested inspection type {type}; raw bytes retained."),
        };
        if (size > end - start) throw new SaveFormatException("Nested member exceeds its parent.");
        return size;
    }
}
