using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.BaldursGate3;

internal sealed record LsmfNumber(string Key, string Label, IReadOnlyList<int> Offsets, long Min, long Max, bool Signed)
{
    internal long Original { get; init; }
    internal string Family { get; init; } = "";
    internal int Row { get; init; }
}

/// <summary>Immutable original model plus sparse, fixed-width patches. No relocation or guessed allocation.</summary>
internal sealed class LsmfLiveSource
{
    private const int HealthRecordBytes = 32;
    private const int StatsRecordBytes = 36;
    private const int AbilityOffset = 8;
    private const int MaxCharacterRows = 100_000;
    private const int MaxHealthCandidate = 1_000_000;
    internal const string Warning = "Not validated on console. PS5 storage joins are sample-proven, but gameplay authority and dependent caches remain unproven. " +
        "Integrity unknown — the game may reject this save or overwrite the edited value. The known outer v18 digest is repaired.";
    private readonly Dictionary<int, uint> _patches = [];
    private readonly List<LsmfNumber> _numbers = [];
    private readonly List<string> _diagnostics = [];
    private readonly LsmfObjectIndex _objects;
    private readonly LarianResource? _resource;
    private readonly Lazy<IReadOnlyList<LsmfEntity>> _entities;
    private readonly HashSet<int> _keyOffsets;

    internal LsmfLiveSource(int entry, int attribute, ReadOnlyMemory<byte> bytes, LarianResource? resource = null)
    {
        Entry = entry;
        Attribute = attribute;
        _resource = resource;
        Access = new LsmfAccess(LsmfReader.Read(bytes));
        _objects = new LsmfObjectIndex(resource);
        _entities = new(() => Access.Model.Entities());
        Owners = Access.Owners();
        Stacks = Optional(() => LsmfInventory.Stacks(Access), "Stacks");
        Slots = Optional(() => LsmfInventory.Slots(Access), "Containers");
        Identity = null;
        Inventory = null;
        try { Identity = new LsmfIdentityGraph(Access, resource); }
        catch (SaveFormatException exception) { _diagnostics.Add("Identity joins: " + exception.Message); }
        if (Identity is not null)
        {
            try { Inventory = new LsmfInventoryGraph(Access, Identity, resource); }
            catch (SaveFormatException exception) { _diagnostics.Add("Inventory joins: " + exception.Message); }
            catch (InvalidDataException exception) { _diagnostics.Add("Inventory joins: " + exception.Message); }
        }
        _keyOffsets = Slots.Select(slot => slot.KeyOffset).ToHashSet();
        Classes = Optional(() => LsmfClasses.Read(Access), "Classes");
        Books = Optional(() => LsmfSpellBooks.Read(Access), "Spell books");
        foreach (var stack in Stacks)
            _numbers.Add(new LsmfNumber(stack.Key + "/quantity", "Stored member quantity", stack.AmountOffsets,
                stack.AmountOffsets.Count, stack.Original, false) { Original = stack.Original, Family = "Stacks", Row = stack.EntityRow });
        ReadCharacters();
        // Reject overlapping editable ranges rather than letting aliases create inconsistent bindings.
        var editableOffsets = _numbers.SelectMany(number => number.Offsets).OrderBy(offset => offset).ToArray();
        BinaryData.Require(editableOffsets.Zip(editableOffsets.Skip(1)).All(pair => pair.Second >= pair.First + sizeof(uint)),
            "Overlapping editable LSMF storage.");
        var numberBytes = _numbers.SelectMany(n => n.Offsets).SelectMany(offset => Enumerable.Range(offset, 4)).ToHashSet();
        BinaryData.Require(Slots.All(slot => !numberBytes.Contains(slot.KeyOffset) && !numberBytes.Contains(slot.KeyOffset + 1)),
            "Container map keys overlap editable value storage.");
    }

    internal int Entry { get; }
    internal int Attribute { get; }
    internal string Prefix => $"live/{Entry}/{Attribute}";
    internal LsmfAccess Access { get; }
    internal IReadOnlyDictionary<int, int[]> Owners { get; }
    internal IReadOnlyList<LsmfStack> Stacks { get; }
    internal IReadOnlyList<LsmfSlot> Slots { get; }
    internal IReadOnlyList<LsmfClass> Classes { get; }
    internal IReadOnlyList<LsmfSpellBook> Books { get; }
    internal LsmfIdentityGraph? Identity { get; }
    internal LsmfInventoryGraph? Inventory { get; }
    internal IReadOnlyList<LsmfNumber> Numbers => _numbers;
    internal IReadOnlyList<string> Diagnostics => _diagnostics;
    internal bool HasEdits => _patches.Count > 0;
    internal string Id(LsmfNumber number) => Prefix + "/" + number.Key;
    internal string EntityLabel(int row) => row >= 0 && row < _entities.Value.Count
        ? $"Entity row {row}: {_objects.Find(_entities.Value[row].Guid) ?? "unresolved object"}; GUID occurrence {_entities.Value[row].Occurrence}/{_entities.Value[row].Copies}"
        : "Unknown entity row";

    internal string Provenance(LsmfNumber number)
    {
        if (number.Family == "Stacks") return EntityLabel(number.Row);
        var name = number.Family == "Health" ? "game.stats.v0.HealthComponent" : "game.stats.v3.StatsComponent";
        var component = Access.Model.Components.Single(c => c.Name == name);
        return Owners.TryGetValue(component.Index, out var owners) && number.Row < owners.Length
            ? "Unvalidated ownerlist position candidate — " + EntityLabel(owners[number.Row])
            : "No unambiguous ownerlist candidate";
    }
    internal long Get(LsmfNumber number) => number.Offsets.Sum(offset => number.Signed
        ? (long)unchecked((int)Read(offset)) : Read(offset));
    internal int Slot(LsmfSlot slot) => (int)(_patches.GetValueOrDefault(slot.KeyOffset,
        BinaryData.U16(Access.Model.Bytes.Span, slot.KeyOffset)) & ushort.MaxValue);

    internal void Set(LsmfNumber number, FieldValue value)
    {
        var next = value.AsInt64();
        BinaryData.Require(next >= number.Min && next <= number.Max, $"Value must be between {number.Min} and {number.Max}.");
        // Keep every contribution positive. Work on a private candidate so a failed setter is atomic.
        var remaining = next;
        var replacements = new Dictionary<int, uint>();
        for (var index = 0; index < number.Offsets.Count; index++)
        {
            var offset = number.Offsets[index];
            var amount = index == number.Offsets.Count - 1 ? remaining :
                Math.Min(BinaryData.U32(Access.Model.Bytes.Span, offset), remaining - (number.Offsets.Count - index - 1));
            BinaryData.Require(amount >= number.Min || number.Offsets.Count > 1 && amount >= 1,
                "Invalid stack contribution distribution.");
            replacements.Add(offset, checked((uint)amount));
            remaining -= amount;
        }
        foreach (var (offset, amount) in replacements) Patch(offset, amount);
    }

    internal void Swap(LsmfSlot first, LsmfSlot second)
    {
        BinaryData.Require(first.ContainerRow == second.ContainerRow && first.Key != second.Key,
            "Select two different entries from one container.");
        var old = Slot(first);
        PatchKey(first.KeyOffset, (ushort)Slot(second));
        PatchKey(second.KeyOffset, (ushort)old);
    }

    internal Dictionary<int, uint> Capture() => new(_patches);
    internal void Restore(IReadOnlyDictionary<int, uint> patches)
    {
        _patches.Clear();
        foreach (var (offset, value) in patches) _patches.Add(offset, value);
    }

    internal byte[] Write()
    {
        var bytes = Access.Model.Bytes.ToArray();
        foreach (var (offset, value) in _patches)
        {
            if (_keyOffsets.Contains(offset))
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), (ushort)value);
            else BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        }
        var reopened = new LsmfLiveSource(Entry, Attribute, bytes, _resource);
        BinaryData.Require(reopened.Diagnostics.SequenceEqual(Diagnostics) && reopened.Numbers.Count == Numbers.Count &&
            Numbers.Zip(reopened.Numbers).All(pair => pair.First.Key == pair.Second.Key && Get(pair.First) == pair.Second.Original) &&
            Slots.Zip(reopened.Slots).All(pair => Slot(pair.First) == pair.Second.Slot),
            "Edited NewAge failed independent structure/value validation.");
        return bytes;
    }

    private void ReadCharacters()
    {
        var health = Access.Column("game.stats.v0.HealthComponent", HealthRecordBytes);
        if (health is not null && health.Rows <= MaxCharacterRows)
        {
            // PS5 profile has no PC stream prefix. The whole nominal section must validate.
            var valid = Enumerable.Range(0, health.Rows).All(row => ValidHealth(health.Offset + row * HealthRecordBytes));
            if (valid)
                for (var row = 0; row < health.Rows; row++)
                {
                    var offset = health.Offset + row * HealthRecordBytes;
                    Add("Health", row, "current", "Current HP candidate", offset, 0, BinaryData.I32(Access.Model.Bytes.Span, offset + 4));
                    Add("Health", row, "temporary", "Temporary HP candidate", offset + 8, 0,
                        Math.Max(BinaryData.I32(Access.Model.Bytes.Span, offset + 8), BinaryData.I32(Access.Model.Bytes.Span, offset + 12)));
                }
            else _diagnostics.Add("Health: unsupported packed profile; no fixed-row edit is exposed.");
        }
        var stats = Access.Column("game.stats.v3.StatsComponent", StatsRecordBytes);
        if (stats is null || stats.Rows > MaxCharacterRows) return;
        string[] abilities = ["Strength", "Dexterity", "Constitution", "Intelligence", "Wisdom", "Charisma"];
        var validStats = Enumerable.Range(0, stats.Rows).All(row => Enumerable.Range(0, abilities.Length)
            .All(ability => BinaryData.I32(Access.Model.Bytes.Span, stats.Offset + row * StatsRecordBytes + AbilityOffset + ability * sizeof(int)) is >= 1 and <= 40));
        if (!validStats) { _diagnostics.Add("Stats: unsupported packed profile; no fixed-row edit is exposed."); return; }
        for (var row = 0; row < stats.Rows; row++)
            for (var ability = 0; ability < abilities.Length; ability++)
                Add("Stats", row, abilities[ability], "Effective " + abilities[ability] + " candidate",
                    stats.Offset + row * StatsRecordBytes + AbilityOffset + ability * sizeof(int), 1,
                    BinaryData.I32(Access.Model.Bytes.Span, stats.Offset + row * StatsRecordBytes + AbilityOffset + ability * sizeof(int)));
    }

    private bool ValidHealth(int offset)
    {
        var bytes = Access.Model.Bytes.Span;
        return BinaryData.I32(bytes, offset) >= 0 && BinaryData.I32(bytes, offset) <= BinaryData.I32(bytes, offset + 4) &&
            BinaryData.I32(bytes, offset + 4) is >= 0 and <= MaxHealthCandidate &&
            BinaryData.I32(bytes, offset + 8) is >= 0 and <= MaxHealthCandidate && BinaryData.I32(bytes, offset + 12) is >= 0 and <= MaxHealthCandidate;
    }

    private void Add(string family, int row, string key, string label, int offset, long min, long max) =>
        _numbers.Add(new LsmfNumber($"{family}/{row}/{key}", label, [offset], min, max, true)
        { Original = BinaryData.I32(Access.Model.Bytes.Span, offset), Family = family, Row = row });

    private IReadOnlyList<T> Optional<T>(Func<IReadOnlyList<T>> read, string label)
    {
        try { return read(); }
        catch (SaveFormatException exception) { _diagnostics.Add($"{label}: {exception.Message}"); return []; }
    }

    private uint Read(int offset) => _patches.GetValueOrDefault(offset, BinaryData.U32(Access.Model.Bytes.Span, offset));
    private void Patch(int offset, uint value)
    {
        if (value == BinaryData.U32(Access.Model.Bytes.Span, offset)) _patches.Remove(offset);
        else _patches[offset] = value;
    }
    private void PatchKey(int offset, ushort value)
    {
        if (value == BinaryData.U16(Access.Model.Bytes.Span, offset)) _patches.Remove(offset);
        else _patches[offset] = value;
    }
}
