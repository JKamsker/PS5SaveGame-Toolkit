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
        var raw = BinaryData.Slice(data, node.ValueOffset, node.ValueSize);
        var classIndex = raw.Length >= 8 ? BinaryData.UInt16(raw, 6) : 0;
        if (raw.Length < 11 || !raw[..2].SequenceEqual(new byte[] { 0, 1 }) ||
            classIndex < 1 || classIndex > names.Count || names[classIndex - 1] != "W3LevelManager")
        {
            throw new SaveFormatException("Unsupported player level manager handle.");
        }

        var position = node.ValueOffset + 8;
        var end = node.Offset + node.Size;
        _manager = TaggedRecord.Read(data, ref position, end, names);
        if (position != end)
        {
            throw new SaveFormatException("Trailing data in player level manager.");
        }

        Level = _originalLevel = _manager.Integer("level");
        var definitions = _manager.Find("levelDefinitions");
        if (definitions is null || definitions.Type != "array:2,0,SLevelDefinition")
        {
            throw new SaveFormatException("Missing player level definitions.");
        }

        var definitionCount = BinaryData.Int32(definitions.Value, 0);
        if (definitionCount is < 1 or > MaximumLevelDefinitions)
        {
            throw new SaveFormatException("Invalid player level definition count.");
        }

        position = definitions.Offset + 12;
        var definitionEnd = definitions.Offset + definitions.Bytes.Length;
        for (var index = 0; index < definitionCount; index++)
        {
            MaximumDefinedLevel = Math.Max(MaximumDefinedLevel, TaggedRecord.Read(data, ref position, definitionEnd, names).Integer("number"));
        }

        if (position != definitionEnd || MaximumDefinedLevel < Level)
        {
            throw new SaveFormatException("Unsupported player level definitions or current level.");
        }

        var pointArray = _manager.Find("points");
        if (pointArray is null || pointArray.Type != "array:2,0,SSpendablePoints" ||
            BinaryData.Int32(pointArray.Value, 0) != SpendablePointTypes)
        {
            throw new SaveFormatException("Unsupported player spendable points array.");
        }

        position = pointArray.Offset + 12;
        var pointEnd = pointArray.Offset + pointArray.Bytes.Length;
        _points = new TaggedRecord[SpendablePointTypes];
        for (var index = 0; index < _points.Length; index++)
        {
            _points[index] = TaggedRecord.Read(data, ref position, pointEnd, names);
        }

        if (position != pointEnd)
        {
            throw new SaveFormatException("Trailing data in player spendable points.");
        }

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
        if (!Changed)
        {
            return [];
        }

        var names = _names.ToList();
        var replacements = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (Level != _originalLevel)
        {
            replacements.Add("level", Integer("level", Level));
        }

        if (Experience != _originalFree[1] || AbilityPoints != _originalFree[0])
        {
            using var values = new MemoryStream();
            values.Write(BinaryData.EncodeInt32(SpendablePointTypes));
            for (var index = 0; index < _points.Length; index++)
            {
                var value = index == 0 ? AbilityPoints : Experience;
                var changes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                if (value != _originalFree[index])
                {
                    changes.Add("free", Integer("free", value));
                }

                values.Write(_points[index].Write(changes));
            }

            replacements.Add("points", TaggedRecord.Property(Name("points"), Name("array:2,0,SSpendablePoints"), values.ToArray()));
        }

        var record = _manager.Write(replacements);
        const int prefixSize = OuterPropertyHeaderSize + HandleHeaderSize;
        var bytes = new byte[prefixSize + record.Length];
        _data.AsSpan(_node.Offset, prefixSize).CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), bytes.Length - OuterPropertyHeaderSize);
        record.CopyTo(bytes, prefixSize);
        var patches = new List<Sav3Patch> { new(_node.Offset, _node.Size, bytes) };
        if (names.Count != _names.Count)
        {
            var table = directory.VariableTableOffset;
            var nm = BinaryData.PayloadOffset(_data, table - 10, container.HeaderSize);
            var oldLength = table - 10 - nm - 2;
            using var manu = new MemoryStream();
            manu.Write(_data.AsSpan(nm + 2, oldLength - 8));
            foreach (var name in names.Skip(_names.Count))
            {
                manu.WriteByte(checked((byte)name.Length));
                manu.Write(System.Text.Encoding.ASCII.GetBytes(name));
            }

            manu.Write(_data.AsSpan(nm + 2 + oldLength - 8, 8));
            var dictionaryBytes = manu.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(dictionaryBytes.AsSpan(4), names.Count);
            patches.Add(new Sav3Patch(nm + 2, oldLength, dictionaryBytes));
        }

        return patches;

        int Name(string name)
        {
            var index = names.IndexOf(name);
            if (index < 0)
            {
                if (names.Count == ushort.MaxValue)
                {
                    throw new SaveFormatException("Witcher name dictionary is full.");
                }

                names.Add(name);
                index = names.Count - 1;
            }

            return index + 1;
        }

        byte[] Integer(string name, int value) => TaggedRecord.Property(Name(name), Name("Int32"), BinaryData.EncodeInt32(value));
    }
}
