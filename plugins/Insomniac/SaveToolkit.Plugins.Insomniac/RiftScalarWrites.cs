using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Insomniac;

/// <summary>Only existing fixed-width atoms. No resizing, unlocks or inventory construction.</summary>
internal sealed class RiftScalarWrites
{
    internal const string Warning = "Not validated on console. Integrity coverage is unknown; the game may reject the edited save. " +
        "Option unlocks and gameplay dependencies are not changed. Owner validation: https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31.";
    // Maximum stored counters in the supplied 36 primary gameplay records, not game caps.
    internal const long ObservedCareerBoltsMaximum = 1_297_735;
    internal const long ObservedSpentBoltsMaximum = 260_875;
    internal const string CareerId = "player/CareerBolts";
    internal const string SpentId = "player/SpentBolts";
    internal const string BalanceId = "player/AvailableBolts";
    private readonly byte[] _bytes;
    private readonly Dictionary<string, ScalarWrite> _writes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ScalarWrite> _diagnostics = new(StringComparer.Ordinal);

    internal RiftScalarWrites(Dat1Container container, DdlSections sections, bool supportedTitle)
    {
        _bytes = container.Bytes;
        if (!supportedTitle) return;
        // Both counters are needed for final-pair validation and the live calculated balance.
        if (sections.Roots.TryGetValue(RiftPlayerView.CampaignTag, out var campaign) &&
            SupportsCounter(campaign["CareerBolts"]) && SupportsCounter(campaign["SpentBolts"]))
        {
            AddCounter(CareerId, campaign["CareerBolts"], ObservedCareerBoltsMaximum);
            AddCounter(SpentId, campaign["SpentBolts"], ObservedSpentBoltsMaximum);
        }
        if (sections.Roots.TryGetValue(RiftPlayerView.GoldBoltTag, out var gold))
        {
            string[] names = ["InfiniteHealthEnabled", "InfiniteAmmoEnabled", "GoldWeaponsEnabled", "MirrorModeEnabled",
                "HomeRunHitterEnabled", "RPGEnabled", "ConfettiHeadshotEnabled"];
            foreach (var name in names)
                if (gold.TryGetValue(name, out var field)) AddBoolean($"player/{name}", RiftPlayerView.GoldBoltTag, field);
        }
        if (sections.Roots.TryGetValue(DdlSections.PreferencesTag, out var preferences))
            foreach (var field in preferences.Values) AddBoolean($"preferences/{field.Name}", DdlSections.PreferencesTag, field);
    }

    internal bool CanEdit => _writes.Count > 0;
    internal bool TryWrite(string id, out ScalarWrite write) => _writes.TryGetValue(id, out write!);
    internal bool TryDiagnostic(string id, out ScalarWrite write) => _diagnostics.TryGetValue(id, out write!);
    internal bool HasBalance => _writes.ContainsKey(CareerId) && _writes.ContainsKey(SpentId);
    internal FieldValue Balance => FieldValue.Of(_writes[CareerId].Value.AsInteger() - _writes[SpentId].Value.AsInteger());

    internal FieldError? ValidateBatch(IReadOnlyList<ValueEdit> edits)
    {
        foreach (var edit in edits)
            if (_writes.TryGetValue(edit.ValueId, out var write) && write.Validate(edit.Value) is { } error)
                return new FieldError(edit.ValueId, error);
        if (!HasBalance || !edits.Any(e => e.ValueId is CareerId or SpentId)) return null;
        var career = _writes[CareerId].Value.AsInteger();
        var spent = _writes[SpentId].Value.AsInteger();
        foreach (var edit in edits)
        {
            if (edit.ValueId == CareerId) career = edit.Value.AsInteger();
            if (edit.ValueId == SpentId) spent = edit.Value.AsInteger();
        }
        // Undo must be able to restore an accepted import, even when its balance was negative.
        // This exception is for the exact original pair, never another negative allocation.
        var restoresOriginal = career == _writes[CareerId].OriginalValue.AsInteger() &&
            spent == _writes[SpentId].OriginalValue.AsInteger();
        return career >= spent || restoresOriginal ? null : new FieldError(CareerId,
            "Career bolts must be at least spent bolts, except when restoring the original imported pair.");
    }

    internal void Set(ScalarWrite write, FieldValue value)
    {
        if (write.Validate(value) is { } error) throw new ArgumentException(error, nameof(value));
        var destination = _bytes.AsSpan(write.Field.Offset);
        switch (write.Field.Kind)
        {
            case DdlValueKind.Bool: destination[0] = value.AsBool() ? (byte)1 : (byte)0; break;
            case DdlValueKind.UInt8: destination[0] = checked((byte)value.AsInteger()); break;
            case DdlValueKind.UInt16: BinaryPrimitives.WriteUInt16LittleEndian(destination, checked((ushort)value.AsInteger())); break;
            case DdlValueKind.UInt32: BinaryPrimitives.WriteUInt32LittleEndian(destination, value.AsUInt32()); break;
            case DdlValueKind.UInt64: BinaryPrimitives.WriteUInt64LittleEndian(destination, value.AsUInt64()); break;
            default: throw new InvalidOperationException("Unsupported scalar write layout.");
        }
        write.Field.SetScalar(value);
    }

    private static bool SupportsCounter(DdlField field) => field.Count == 1 && field.Values.Count == 1 &&
        field.Kind is DdlValueKind.UInt8 or DdlValueKind.UInt16 or DdlValueKind.UInt32 or DdlValueKind.UInt64;

    private void AddCounter(string id, DdlField field, long observedMaximum)
    {
        var wire = (IntegerSpec)field.Spec;
        var maximum = Int128.Min(wire.Max, Int128.Max(observedMaximum, field.Values[0].AsInteger()));
        Add(id, RiftPlayerView.CampaignTag, field, wire with { Max = maximum });
    }

    private void AddBoolean(string id, uint tag, DdlField field)
    {
        if (field.Kind == DdlValueKind.Bool && field.Count == 1 && field.Values.Count == 1)
            Add(id, tag, field, new BoolSpec());
    }

    private void Add(string id, uint tag, DdlField field, FieldSpec spec)
    {
        var write = new ScalarWrite(field, spec);
        _writes.Add(id, write);
        _diagnostics.Add($"ddl/{tag:x8}/{Uri.EscapeDataString(field.Name)}/0", write);
    }
}

internal sealed record ScalarWrite(DdlField Field, FieldSpec Spec)
{
    internal FieldValue OriginalValue { get; } = Field.Values[0];
    internal FieldValue Value => Field.Values[0];
    internal string? Validate(FieldValue value) => Spec switch
    {
        BoolSpec when value.Kind != FieldValueKind.Bool => "Expected a boolean value.",
        IntegerSpec when value.Kind != FieldValueKind.Integer => "Expected an integer value.",
        IntegerSpec bounds when value.AsInteger() < bounds.Min || value.AsInteger() > bounds.Max =>
            $"Value must be between {bounds.Min} and {bounds.Max}; width and conservative observed bounds are retained.",
        _ => null,
    };
}
