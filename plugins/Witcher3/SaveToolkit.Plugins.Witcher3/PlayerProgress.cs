using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed class PlayerProgress
{
    private const int SpendablePointTypes = 2;
    private const int MaximumLevelDefinitions = 4096;
    private const int OuterPropertyHeaderSize = 12;
    private const int HandleHeaderSize = 8;
    private readonly Sav3Node _node;
    private readonly byte[] _data;
    private readonly IReadOnlyList<string> _names;
    private readonly TaggedRecord _manager;
    private readonly TaggedRecord[] _points;
    private readonly int _originalLevel;
    private readonly int[] _originalFree;

    public PlayerProgress(Sav3Node node, byte[] data, IReadOnlyList<string> names)
    {
        _node = node;
        _data = data;
        _names = names;
        _manager = ReadManager(node, data, names);
        Level = _originalLevel = _manager.Integer("level");
        MaximumDefinedLevel = ReadMaximumLevel(_manager, data, names, Level);
        _points = ReadPoints(_manager, data, names);

        _originalFree = _points.Select(p => p.Integer("free")).ToArray();
        AbilityPoints = _originalFree[0];
        Experience = _originalFree[1];
        UsedExperience = _points[1].Integer("used");
        UsedAbilityPoints = _points[0].Integer("used");
        if (Level < 1 || _originalFree.Any(p => p < 0) || UsedExperience < 0 || UsedAbilityPoints < 0 ||
            (long)Experience + UsedExperience > int.MaxValue || (long)AbilityPoints + UsedAbilityPoints > int.MaxValue)
        {
            throw new SaveFormatException("Invalid negative or overflowing player progression.");
        }
    }

    public int Level { get; set; }
    public int MaximumDefinedLevel { get; }
    public int Experience { get; set; }
    public int AbilityPoints { get; set; }
    public int UsedExperience { get; }
    public int UsedAbilityPoints { get; }
    public bool Changed => Level != _originalLevel || Experience != _originalFree[1] || AbilityPoints != _originalFree[0];

    public IReadOnlyList<Sav3Patch> Patches(Sav3Directory directory, WitcherContainer container)
    {
        if (!Changed) { return []; }
        var names = new NameTableEdits(_names);
        var replacements = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (Level != _originalLevel) { replacements.Add("level", names.IntegerProperty("level", Level)); }
        if (Experience != _originalFree[1] || AbilityPoints != _originalFree[0])
        {
            replacements.Add("points", PointsProperty(names));
        }

        var record = _manager.Write(replacements);
        const int prefixSize = OuterPropertyHeaderSize + HandleHeaderSize;
        var bytes = new byte[prefixSize + record.Length];
        _data.AsSpan(_node.Offset, prefixSize).CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), bytes.Length - OuterPropertyHeaderSize);
        record.CopyTo(bytes, prefixSize);
        var patches = new List<Sav3Patch> { new(_node.Offset, _node.Size, bytes) };
        if (names.Patch(_data, directory, container.HeaderSize) is { } dictionary) { patches.Add(dictionary); }
        return patches;
    }

    private byte[] PointsProperty(NameTableEdits names)
    {
        using var values = new MemoryStream();
        values.Write(BinaryData.EncodeInt32(SpendablePointTypes));
        for (var index = 0; index < _points.Length; index++)
        {
            var value = index == 0 ? AbilityPoints : Experience;
            var changes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            if (value != _originalFree[index]) { changes.Add("free", names.IntegerProperty("free", value)); }
            values.Write(_points[index].Write(changes));
        }

        return TaggedRecord.Property(names.Index("points"), names.Index("array:2,0,SSpendablePoints"), values.ToArray());
    }

    private static TaggedRecord ReadManager(Sav3Node node, byte[] data, IReadOnlyList<string> names)
    {
        var raw = BinaryData.Slice(data, node.ValueOffset, node.ValueSize);
        var classIndex = raw.Length >= 8 ? BinaryData.UInt16(raw, 6) : 0;
        if (raw.Length < 11 || !raw[..2].SequenceEqual(new byte[] { 0, 1 }) ||
            classIndex < 1 || classIndex > names.Count || names[classIndex - 1] != "W3LevelManager")
        {
            throw new SaveFormatException("Unsupported player level manager handle.");
        }

        var position = node.ValueOffset + HandleHeaderSize;
        var end = node.Offset + node.Size;
        var manager = TaggedRecord.Read(data, ref position, end, names);
        if (position != end)
        {
            throw new SaveFormatException("Trailing data in player level manager.");
        }

        return manager;
    }

    private static int ReadMaximumLevel(TaggedRecord manager, byte[] data, IReadOnlyList<string> names, int level)
    {
        var definitions = manager.Find("levelDefinitions");
        if (definitions is null || definitions.Type != "array:2,0,SLevelDefinition")
        {
            throw new SaveFormatException("Missing player level definitions.");
        }

        var definitionCount = BinaryData.Int32(definitions.Value, 0);
        if (definitionCount is < 1 or > MaximumLevelDefinitions)
        {
            throw new SaveFormatException("Invalid player level definition count.");
        }

        var position = definitions.Offset + OuterPropertyHeaderSize;
        var maximum = 0;
        var definitionEnd = definitions.Offset + definitions.Bytes.Length;
        for (var index = 0; index < definitionCount; index++)
        {
            maximum = Math.Max(maximum, TaggedRecord.Read(data, ref position, definitionEnd, names).Integer("number"));
        }

        if (position != definitionEnd || maximum < level)
        {
            throw new SaveFormatException("Unsupported player level definitions or current level.");
        }

        return maximum;
    }

    private static TaggedRecord[] ReadPoints(TaggedRecord manager, byte[] data, IReadOnlyList<string> names)
    {
        var pointArray = manager.Find("points");
        if (pointArray is null || pointArray.Type != "array:2,0,SSpendablePoints" ||
            BinaryData.Int32(pointArray.Value, 0) != SpendablePointTypes)
        {
            throw new SaveFormatException("Unsupported player spendable points array.");
        }

        var position = pointArray.Offset + OuterPropertyHeaderSize;
        var pointEnd = pointArray.Offset + pointArray.Bytes.Length;
        var points = new TaggedRecord[SpendablePointTypes];
        for (var index = 0; index < points.Length; index++)
        {
            points[index] = TaggedRecord.Read(data, ref position, pointEnd, names);
        }

        if (position != pointEnd)
        {
            throw new SaveFormatException("Trailing data in player spendable points.");
        }

        return points;
    }
}
