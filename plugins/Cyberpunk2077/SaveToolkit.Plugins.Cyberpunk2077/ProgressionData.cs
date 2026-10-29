using System.Buffers.Binary;
using System.Collections.Immutable;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed record ProgressionScalar(string Id, string Label, int Value, int Offset, int? MirrorOffset);
internal sealed class PointBalance(string type, PackageClass record)
{
    private const string UnspentName = "unspent";
    private const string Int32Name = "Int32";

    public string Type { get; } = type;
    public PackageClass Record { get; } = record;
    public int Original { get; } = record.Int(UnspentName);
    public int Value { get; set; } = record.Int(UnspentName);
    public bool Changed => Value != Original;
    public IReadOnlyList<string> RequiredNames => Changed ? [UnspentName, Int32Name] : [];
}

internal sealed class ProgressionData
{
    private static readonly ImmutableArray<string> ProficiencyTypes = ["Level", "StreetCred"];
    private readonly CsavNode _node;
    private readonly RedPackage _package;
    private readonly int _playerIndex;
    private readonly PackageClass _player;

    private ProgressionData(CsavContainer container, CsavTree tree)
    {
        var nodes = tree.Nodes.Where(n => n.Name == "ScriptableSystemsContainer").ToArray();
        if (nodes.Length != 1)
        {
            throw new SaveFormatException("Progression editing requires one ScriptableSystemsContainer.");
        }

        _node = nodes[0];
        _package = new RedPackage(PackageBytes(container.Payload, _node), true);
        _playerIndex = ReadPlayerIndex();
        _player = _package.Chunk(_playerIndex);
        ValidatePlayerOwner();
        Points = ReadPoints();

        // Resolve both stored attribute copies. A disagreement disables attribute editing, never guesses.
        var attributes = _player.StructArray(_player.Require("attributes", "array:SAttribute"));
        var mirrors = ReadMirrors(container, tree, out var attributeWarning);
        AttributeWarning = attributeWarning;
        Attributes = ReadAttributes(container, attributes, mirrors);
        Proficiencies = ReadProficiencies(container, mirrors);
    }

    private int ReadPlayerIndex()
    {
        var systems = _package.Types.Select((type, index) => (type, index)).Where(p => p.type == "PlayerDevelopmentSystem").ToArray();
        if (systems.Length != 1)
        {
            throw new SaveFormatException("Progression editing requires one PlayerDevelopmentSystem.");
        }

        var system = _package.Chunk(systems[0].index);
        var handles = system.Raw(system.Require("playerData", "array:handle:PlayerDevelopmentData"));
        if (handles.Length != 8 || BinaryData.Int32(handles, 0) != 1)
        {
            throw new SaveFormatException("Only the verified single-player development layout can be edited.");
        }

        var index = BinaryData.Int32(handles, 4);
        if (index < 0 || index >= _package.Types.Count || _package.Types[index] != "PlayerDevelopmentData")
        {
            throw new SaveFormatException("Player development handle has an invalid target.");
        }

        return index;
    }

    private void ValidatePlayerOwner()
    {
        var owner = _player.Require("ownerID", "entEntityID");
        var ownerBytes = _player.Raw(owner);
        var ownerClass = new PackageClass(_package, ownerBytes, 0, ownerBytes.Length);
        if (BinaryData.UInt64(ownerClass.Raw(ownerClass.Require("hash", "Uint64")), 0) != 1)
        {
            throw new SaveFormatException("Progression editing requires player owner ID 1.");
        }
    }

    private IReadOnlyList<PointBalance> ReadPoints()
    {
        var points = _player.StructArray(_player.Require("devPoints", "array:SDevelopmentPoints"))
            .Select(record => new PointBalance(record.Enum("type", "gamedataDevelopmentPointType", "Attribute"), record)).ToArray();
        if (points.Select(p => p.Type).Distinct(StringComparer.Ordinal).Count() != points.Length ||
            points.Any(p => p.Original < 0 || p.Record.Int("spent") < 0))
        {
            throw new SaveFormatException("Ambiguous or negative development point balances.");
        }

        return points;
    }

    private static IReadOnlyDictionary<ulong, int> ReadMirrors(CsavContainer container, CsavTree tree, out string? warning)
    {
        warning = null;
        try { return PlayerStatsReader.Read(container, tree); }
        catch (SaveFormatException ex) { warning = ex.Message; return new Dictionary<ulong, int>(); }
    }

    private IReadOnlyList<ProgressionScalar> ReadAttributes(CsavContainer container,
        IReadOnlyList<PackageClass> attributes, IReadOnlyDictionary<ulong, int> mirrors)
    {
        var scalars = new List<ProgressionScalar>();
        foreach (var (name, label) in new[] { ("Strength", "Body"), ("Reflexes", "Reflexes"),
            ("TechnicalAbility", "Technical ability"), ("Intelligence", "Intelligence"), ("Cool", "Cool") })
        {
            var records = attributes.Where(a => a.Enum("attributeName", "gamedataStatType", "Invalid") == name).ToArray();
            if (records.Length != 1)
            {
                throw new SaveFormatException($"Missing or ambiguous {name} attribute.");
            }

            var record = records[0];
            var field = record.Require("value", "Int32");
            var value = record.Int("value");
            int? mirror = mirrors.TryGetValue(PlayerStatsReader.NameHash(name), out var offset) &&
                BinaryPrimitives.ReadSingleLittleEndian(container.Payload.AsSpan(offset, 4)) == value ? offset : null;
            scalars.Add(new ProgressionScalar($"attributes/{name}", label, value, _node.Offset + 8 + field.Offset, mirror));
        }

        return scalars;
    }

    private ProficiencyValue[] ReadProficiencies(CsavContainer container, IReadOnlyDictionary<ulong, int> mirrors)
    {
        var proficiencies = _player.StructArray(_player.Require("proficiencies", "array:SProficiency"));
        return ProficiencyTypes.Select(type =>
        {
            var matching = proficiencies.Where(p => p.Enum("type", "gamedataProficiencyType", "Assault") == type).ToArray();
            if (matching.Length != 1)
            {
                throw new SaveFormatException($"Missing or ambiguous {type} proficiency.");
            }

            var record = matching[0];
            var hasStat = mirrors.TryGetValue(PlayerStatsReader.NameHash(type), out var offset);
            int? mirror = hasStat &&
                BinaryPrimitives.ReadSingleLittleEndian(container.Payload.AsSpan(offset, 4)) == record.Int("currentLevel")
                    ? offset : null;
            var levelWarning = AttributeWarning ?? (hasStat && mirror is null
                ? "Stored level disagrees with its forced player stat; level edit refused rather than choosing a copy." : null);
            return new ProficiencyValue(type, record, mirror, levelWarning);
        }).ToArray();
    }

    public IReadOnlyList<PointBalance> Points { get; }
    public IReadOnlyList<ProgressionScalar> Attributes { get; }
    public IReadOnlyList<ProficiencyValue> Proficiencies { get; }
    public string? AttributeWarning { get; }

    public static ProgressionData? TryRead(CsavContainer container, CsavTree tree, out string? warning)
    {
        warning = null;
        if (!InventoryReader.IsVerifiedVersion(container))
        {
            warning = $"Edits refused for save {container.SaveVersion} / game {container.GameVersion} / archive {container.ArchiveVersion}. " +
                "Only PS5 save 265 / game 2210 / archive 195 is verified.";
            return null;
        }

        var nodes = tree.Nodes.Where(n => n.Name == "ScriptableSystemsContainer").ToArray();
        if (nodes.Length != 1)
        {
            warning = "Progression edits refused: no unique ScriptableSystemsContainer was found.";
            return null;
        }

        var bytes = PackageBytes(container.Payload, nodes[0]);
        BinaryData.Slice(bytes, 0, 4);
        if (bytes[0] != 4 || bytes[1] != 0 || RedPackage.UInt16(bytes, 2) != 6)
        {
            warning = $"Progression edits refused for RED package {bytes[0]} / flags {bytes[1]} / sections {RedPackage.UInt16(bytes, 2)}. " +
                "Only package 4 / flags 0 / sections 6 is verified.";
            return null;
        }

        return new ProgressionData(container, tree);
    }

    public KeyValuePair<int, byte[]>? Rebuild(byte[] editedPayload)
    {
        if (Points.All(p => p.Value == p.Original) && Proficiencies.All(p => !p.Changed))
        {
            return null;
        }

        // Read from the patched payload so attribute writes in this same batch are retained.
        var requiredNames = Proficiencies.SelectMany(p => p.RequiredNames)
            .Concat(Points.SelectMany(point => point.RequiredNames));
        var package = new RedPackage(PackageBytes(editedPayload, _node), true).WithNames(requiredNames);
        var player = package.Chunk(_playerIndex);
        var points = RebuildPoints();
        var replacements = new Dictionary<string, byte[]>();
        if (points is not null) replacements.Add("devPoints", points);
        if (Proficiencies.Any(p => p.Changed)) replacements.Add("proficiencies", RebuildProficiencies(player));
        var rebuilt = package.Rebuild(_playerIndex, player.Rebuild(replacements));
        var result = new byte[rebuilt.Length + 8];
        BinaryPrimitives.WriteInt32LittleEndian(result, _node.Id);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), rebuilt.Length);
        rebuilt.CopyTo(result, 8);
        return new(_node.Id, result);
    }

    private byte[]? RebuildPoints()
    {
        using var array = new MemoryStream();
        Span<byte> integer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(integer, Points.Count);
        array.Write(integer);
        foreach (var point in Points)
        {
            BinaryPrimitives.WriteInt32LittleEndian(integer, point.Value);
            array.Write(point.Record.Rebuild(point.Value == point.Original
                ? new Dictionary<string, byte[]>() : new Dictionary<string, byte[]> { ["unspent"] = integer.ToArray() }));
        }

        return Points.Any(p => p.Value != p.Original) ? array.ToArray() : null;
    }

    private byte[] RebuildProficiencies(PackageClass player)
    {
        using var proficiencies = new MemoryStream();
        var records = player.StructArray(player.Require("proficiencies", "array:SProficiency"));
        Span<byte> integer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(integer, records.Count);
        proficiencies.Write(integer);
        foreach (var record in records)
        {
            var type = record.Enum("type", "gamedataProficiencyType", "Assault");
            var editable = Proficiencies.SingleOrDefault(p => p.Type == type);
            proficiencies.Write(editable is null ? record.Rebuild(new Dictionary<string, byte[]>()) : editable.Rebuild(record));
        }
        return proficiencies.ToArray();
    }

    internal static byte[] PackageBytes(byte[] payload, CsavNode node)
    {
        if (node.Children.Count != 0 || BinaryData.Int32(payload, node.Offset + 4) != node.Size - 8)
        {
            throw new SaveFormatException("RED package node size or child layout is unverified.");
        }

        return BinaryData.Slice(payload, node.Offset + 8, node.Size - 8).ToArray();
    }
}
