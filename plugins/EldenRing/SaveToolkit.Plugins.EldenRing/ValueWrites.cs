using System.Buffers.Binary;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing;

internal delegate FieldNode BindWrite(string id, string label, FieldSpec spec, Func<FieldValue> get,
    Action<FieldValue>? set, Func<FieldValue, string?>? validate);

internal sealed class ValueWrites
{
    public const string Warning = "Experimental. Not validated on console. Based on the pinned ClayAmore "
        + "PlayStation layout and one PS5 snapshot. No PS per-slot checksum is known; any additional "
        + "integrity/dependency rules are unknown and the game may reject the edited save. "
        + "Source: ClayAmore / ER-Save-Editor (MIT), revision 014107f0, retrieved 2026-10-28: "
        + "https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/save/common/save_slot.rs";
    public const string QuantityWarning = Warning + " Legal carry/storage caps are unverified; only positive "
        + "existing ordinary stacks are editable, up to conservative observed ceilings (Rowa Fruit 12; Smithing Stone [1] 3).";
    public const uint ObservedRuneHistory = 4427;
    private readonly byte[] _bytes;
    private readonly ParsedSave _save;
    private readonly Dictionary<int, NameWriter> _names;
    private readonly Dictionary<int, uint> _historyCeilings;
    private readonly Dictionary<string, (int Slot, InventoryEntry Entry, uint Ceiling)> _quantities = [];
    private readonly Dictionary<(int Slot, int Offset), QuantityEligibility> _eligibility = [];

    public ValueWrites(byte[] bytes, ParsedSave save, IReadOnlyList<SemanticCharacter> characters)
    {
        _bytes = bytes;
        _save = save;
        _names = save.Slots.Where(slot => slot.Active).ToDictionary(slot => slot.Index, slot => new NameWriter(bytes, slot));
        _historyCeilings = save.Slots.Where(slot => slot.Active).ToDictionary(slot => slot.Index,
            slot => Math.Max(ObservedRuneHistory, Read(slot.PlayerOffset!.Value + EldenRingLayout.RuneMemoryOffset)));
        foreach (var character in characters)
        foreach (var container in character.Containers)
        {
            var handleCounts = container.Entries.GroupBy(entry => entry.Handle).ToDictionary(group => group.Key, group => group.Count());
            foreach (var entry in container.Entries)
            {
                var eligibility = QuantityEligibility.Evaluate(entry, container, handleCounts[entry.Handle], save.CanEdit);
                _eligibility.Add((character.Slot, entry.Offset), eligibility);
                if (eligibility.IsEligible)
                    _quantities.Add(entry.Key(character.Slot), (character.Slot, entry, eligibility.Ceiling));
            }
        }
    }

    public IReadOnlyList<(string Key, int Slot, InventoryEntry Entry, uint Ceiling)> Quantities =>
        _quantities.Select(pair => (pair.Key, pair.Value.Slot, pair.Value.Entry, pair.Value.Ceiling)).ToArray();

    public uint HistoryCeiling(CharacterSlot slot) => _historyCeilings[slot.Index];
    public uint RuneCeiling(CharacterSlot slot) => Math.Min(HistoryCeiling(slot), EldenRingLayout.MaximumRunes);
    public uint Current(InventoryEntry entry) => Read(entry.Offset + sizeof(uint));
    public QuantityEligibility Eligibility(InventoryEntry entry, int slot) => _eligibility[(slot, entry.Offset)];
    public static string QuantityId(InventoryEntry entry, int slot) => entry.Key(slot) + "/quantity";

    public FieldNode Name(CharacterSlot slot, BindWrite bind) => bind($"slots/{slot.Index}/name", "Name",
        new TextSpec { MaxLength = EldenRingLayout.NameCharacters },
        () => FieldValue.Of(EldenRingParser.ReadName(_bytes, slot.ProfileOffset)),
        _save.CanEdit ? _names[slot.Index].Set : null, _names[slot.Index].Validate) with
    {
        Confidence = Confidence.Inferred,
        Description = Warning + " Updates live/menu UTF-16LE names and terminators, preserving each original buffer's trailing bytes.",
        Warning = Warning,
    };

    public FieldNode History(CharacterSlot slot, BindWrite bind) => bind($"slots/{slot.Index}/rune-memory", "Lifetime rune total",
        new IntegerSpec(0, HistoryCeiling(slot)),
        () => FieldValue.Of((long)Read(slot.PlayerOffset!.Value + EldenRingLayout.RuneMemoryOffset)),
        _save.CanEdit ? value => SetHistory(slot, value.AsUInt32()) : null,
        value => IntegerError(value, 0, HistoryCeiling(slot))) with
    {
        Confidence = Confidence.Inferred,
        Description = Warning + " Updates both history mirrors. Must cover held runes; ceiling is the larger of imported history "
            + "and the observed 4427. Earn/spend/loss semantics remain unverified; serialization never adds history.",
        Warning = Warning,
    };

    public FieldNode Quantity(InventoryEntry entry, int slot, BindWrite bind)
    {
        var eligibility = Eligibility(entry, slot);
        var eligible = eligibility.IsEligible;
        return bind(QuantityId(entry, slot), "Quantity", new IntegerSpec(eligible ? 1 : 0, eligible ? eligibility.Ceiling : uint.MaxValue),
            () => FieldValue.Of((long)Current(entry)),
            eligibility.CanEdit ? value => Write(entry.Offset + sizeof(uint), value.AsUInt32()) : null,
            value => IntegerError(value, 1, eligibility.Ceiling)) with
        {
            Confidence = eligible ? Confidence.Inferred : Confidence.Unknown,
            Description = eligibility.Description + (eligible ? " " + QuantityWarning : ""),
            Warning = eligible ? QuantityWarning : "No structural insertion, removal, transfer or container-managed edits.",
        };
    }

    public void RegisterQuantities(BindWrite bind)
    {
        foreach (var (_, slot, entry, _) in Quantities) _ = Quantity(entry, slot, bind);
    }

    public static string? IntegerError(FieldValue value, uint minimum, uint maximum) =>
        value.Kind != FieldValueKind.Integer || value.AsInteger() < minimum || value.AsInteger() > maximum
            ? $"Choose a whole number from {minimum} to {maximum}." : null;

    private void SetHistory(CharacterSlot slot, uint value)
    {
        Write(slot.PlayerOffset!.Value + EldenRingLayout.RuneMemoryOffset, value);
        Write(slot.ProfileOffset + EldenRingLayout.ProfileRuneMemoryOffset, value);
    }

    private uint Read(int offset) => EldenRingParser.ReadUInt32(_bytes, offset);
    private void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(offset), value);
}
