using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed class SkillRank(AbilityEntry entry, int maximum)
{
    public AbilityEntry Entry { get; } = entry;
    public int Original { get; } = entry.Record.Integer("level");
    public int Value { get; set; } = entry.Record.Integer("level");
    public int Maximum { get; } = maximum;
    public string ValueId => $"abilities/skills/{Entry.Id}/level";
}

/// <summary>Changes only an existing skill definition's rank; omitted zero ranks are inserted losslessly.</summary>
internal sealed class SkillRanks
{
    private const int HandleHeaderSize = 8;
    private const int PropertyHeaderSize = 8;
    private const string LevelName = "level";
    private const string IntegerType = "Int32";
    private readonly IReadOnlyList<string> _names;
    private readonly TaggedProperty _property;
    private readonly AbilityArray _skills;
    public IReadOnlyList<SkillRank> Ranks { get; }
    private readonly IReadOnlyDictionary<string, SkillRank> _byId;
    public SkillRank? Find(string id) => _byId.GetValueOrDefault(id);

    public SkillRanks(Sav3Node manager, byte[] data, IReadOnlyList<string> names, AbilityArray skills)
    {
        _names = names;
        _skills = skills;
        var position = manager.ValueOffset + HandleHeaderSize;
        var record = TaggedRecord.Read(data, ref position, manager.ValueOffset + manager.ValueSize, names);
        _property = record.Find("skills") ?? throw new SaveFormatException("Missing saved skills array.");
        var ranks = new List<SkillRank>();
        foreach (var entry in skills.Entries)
        {
            // Sentinels and core definitions are not purchased ranks. Only the exact observed family is writable.
            if (CoreIdentity(entry.Id) || entry.Record.Find("skillType") is not { Type: "ESkill" } identity || identity.Value.Length != sizeof(ushort)) { continue; }
            if (entry.Record.Find("isCoreSkill") is { } core)
            {
                if (core.Type != "Bool" || core.Value.Length != 1 || core.Value[0] > 1)
                {
                    throw new SaveFormatException("Invalid saved core-skill flag.");
                }
                if (core.Value[0] != 0) { continue; }
            }

            var maximum = entry.Record.Integer("maxLevel");
            var level = entry.Record.Integer(LevelName);
            if (maximum > 0 && level >= 0 && level <= maximum && names.Contains(LevelName) && names.Contains(IntegerType))
            {
                ranks.Add(new SkillRank(entry, maximum));
            }
        }

        Ranks = ranks;
        _byId = ranks.ToDictionary(r => r.Entry.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<Sav3Patch> Patches()
    {
        if (Ranks.All(r => r.Value == r.Original)) { return []; }
        using var values = new MemoryStream();
        values.Write(BinaryData.EncodeInt32(_skills.Entries.Count));
        foreach (var entry in _skills.Entries)
        {
            var rank = Find(entry.Id);
            values.Write(rank is null || rank.Value == rank.Original ? entry.Record.Write(new Dictionary<string, byte[]>()) :
                entry.Record.Write(new Dictionary<string, byte[]> { [LevelName] = LevelProperty(rank.Value) }));
        }

        var bytes = new byte[PropertyHeaderSize + values.Length];
        _property.Bytes.AsSpan(0, PropertyHeaderSize).CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(sizeof(int)), bytes.Length - sizeof(int));
        values.ToArray().CopyTo(bytes, PropertyHeaderSize);
        return [new Sav3Patch(_property.Offset, _property.Bytes.Length, bytes)];
    }

    public void Verify(IReadOnlyList<AbilityArray> reopened)
    {
        var skills = reopened.SingleOrDefault(a => a.Name == "skills");
        if (skills is null || !_skills.Entries.Select(e => e.Id).SequenceEqual(skills.Entries.Select(e => e.Id)))
        {
            throw new SaveFormatException("Skill identities changed during serialization.");
        }

        foreach (var (before, after) in _skills.Entries.Zip(skills.Entries))
        {
            var rank = Find(before.Id);
            var replacements = new Dictionary<string, byte[]>();
            if (rank is not null && rank.Value != rank.Original) { replacements.Add(LevelName, LevelProperty(rank.Value)); }
            if (!before.Record.Write(replacements).AsSpan().SequenceEqual(after.Record.Write(new Dictionary<string, byte[]>())))
            {
                throw new SaveFormatException("Edited skill rank or unrelated skill metadata did not survive serialization.");
            }
        }
    }

    private byte[] LevelProperty(int value) => TaggedRecord.Property(
        NameIndex(LevelName), NameIndex(IntegerType), BinaryData.EncodeInt32(value));

    // The converter's native 102-skill profile and all supplied saves agree on these base definitions.
    private static bool CoreIdentity(string id) => id is "S_Perk_08" or
        "S_Sword_1" or "S_Sword_2" or "S_Sword_3" or "S_Sword_4" or "S_Sword_5" or
        "S_Magic_1" or "S_Magic_2" or "S_Magic_3" or "S_Magic_4" or "S_Magic_5" or
        "S_Alchemy_1" or "S_Alchemy_2" or "S_Alchemy_3" or "S_Alchemy_4" or "S_Alchemy_5";

    private int NameIndex(string name)
    {
        for (var index = 0; index < _names.Count; index++)
        {
            if (_names[index] == name) { return index + 1; }
        }
        throw new SaveFormatException($"Missing '{name}' dictionary entry for skill rank.");
    }
}
