using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

internal sealed class GearboxDocument(string relativePath, string titleId, GearboxContainer container, ProtoMessage root) : BoundSaveDocument
{
    private readonly Dictionary<string, Func<FieldValue>> _readers = new(StringComparer.Ordinal);
    private readonly GearboxLazyViews _lazy = new();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public ProtoMessage Root { get; private set; } = root;
    public bool Profile => container.Profile;
    public override string Title => Profile ? "Gearbox profile" : Root.Text("preferred_character_name");

    public override ViewNode BuildView()
    {
        var overview = new GearboxOverview(this);
        var tables = new GearboxTables(this, _lazy);
        return new TabsNode([
            new Tab(Profile ? "Profile" : "Character", overview.Build()),
            new Tab("Inventory", tables.Inventory()),
            new Tab("Progression", tables.Progression()),
            new Tab("Raw", [_lazy.RegisterRaw(() => Root)]),
        ]);
    }

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) => _lazy.Expand(lazyId);

    internal FieldNode Integer(string id, string label, Func<ProtoMessage> parent, string name,
        long max = int.MaxValue, bool writable = true, string? warning = null)
    {
        var current = parent().Integer(name);
        return Bind(id, label, new IntegerSpec(Math.Min(0, current), Math.Max(max, current)), () => FieldValue.Of(parent().Integer(name)),
            writable && current >= 0 && current <= max ? v => parent().SetInteger(name, v.AsInt64()) : null,
            v => v.Kind != FieldValueKind.Integer || v.AsInteger() < 0 || v.AsInteger() > max ? "Value is outside the supported range." : null,
            warning);
    }

    internal FieldNode Text(string id, string label, Func<ProtoMessage> parent, string name, bool writable = false, int limit = 64)
    {
        return Bind(id, label, new TextSpec { MaxLength = writable ? limit : null }, () => FieldValue.Of(parent().Text(name)),
            writable ? v => parent().SetText(name, v.AsString()) : null,
            v => ValidateName(v, limit));
    }

    private static string? ValidateName(FieldValue value, int limit)
    {
        if (value.Kind != FieldValueKind.Text || value.AsString().Length > limit || value.AsString().Any(char.IsControl))
            return "Use a name within the displayed limit, without control characters.";
        try { _ = StrictUtf8.GetByteCount(value.AsString()); }
        catch (EncoderFallbackException) { return "The name contains invalid Unicode."; }
        return null;
    }

    internal FieldNode Ammo(string id, Func<ProtoMessage> parent)
    {
        const int maximumAmmo = 100_000;
        var current = parent().Float("amount");
        var editable = float.IsFinite(current) && current is >= 0 and <= maximumAmmo;
        return Bind(id, "Ammo", FloatSpec.Single with { Min = Math.Min(0, current), Max = Math.Max(maximumAmmo, current) },
            () => FieldValue.Of(parent().Float("amount")), editable ? v => parent().SetFloat("amount", (float)v.AsDouble()) : null,
            v => !double.IsFinite(v.AsDouble()) || v.AsDouble() is < 0 or > maximumAmmo ? "Unsupported ammo value." : null,
            "The game may clamp ammo to the capacity provided by its SDU.");
    }

    internal FieldNode Level()
    {
        var schema = Root.Schema;
        return Bind("character/level", "Level", new IntegerSpec(1, schema.Experience.Count),
            () => FieldValue.Of(schema.Level(checked((int)Root.Integer("experience_points")))),
            v => CharacterProgression.SetExperience(Root, schema.Experience[v.AsInt32() - 1]),
            v => v.Kind != FieldValueKind.Integer || v.AsInteger() < 1 || v.AsInteger() > schema.Experience.Count ? "Unsupported level." : null,
            "Adjusts XP and the recorded level; Wonderlands awards skill points when levelling up. Lowering level preserves spent points. Milestone challenges and mission unlocks are unchanged.");
    }

    internal FieldNode Experience() => Bind("character/xp", "Experience points", new IntegerSpec(0, int.MaxValue),
        () => FieldValue.Of(Root.Integer("experience_points")), v => CharacterProgression.SetExperience(Root, v.AsInt32()),
        v => v.Kind != FieldValueKind.Integer || v.AsInteger() < 0 || v.AsInteger() > int.MaxValue ? "Unsupported XP value." : null,
        "Changing XP can change level. Values beyond the supported level cap remain experimental.");

    internal FieldNode Category(string list, uint hash, string label, long maximum, string id)
    {
        var current = Root.CategoryQuantity(list, hash);
        return Bind(id, label, new IntegerSpec(Math.Min(0, current), Math.Max(maximum, current)),
            () => FieldValue.Of(Root.CategoryQuantity(list, hash)),
            current >= 0 && current <= maximum ? v => Root.SetCategoryQuantity(list, hash, v.AsInt64()) : null,
            v => v.Kind != FieldValueKind.Integer || v.AsInteger() < 0 || v.AsInteger() > maximum ? "Unsupported currency amount." : null,
            "The game may clamp large values. Absent currency categories are created only when a nonzero amount is set.");
    }

    private FieldNode Bind(string id, string label, FieldSpec spec, Func<FieldValue> get, Action<FieldValue>? set,
        Func<FieldValue, string?>? validate = null, string? warning = null)
    {
        _readers[id] = get;
        return Field(id, label, spec, get, set, validate) with { Confidence = Confidence.Verified, Warning = warning };
    }

    public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        var before = _readers.ToDictionary(p => p.Key, p => p.Value());
        var previous = Root;
        Root = Root.Clone();
        // Replayed derived fields (XP, skill points, recorded level) override automatic level adjustments.
        var ordered = edits.OrderBy(e => e.ValueId == "character/level" ? 0 : e.ValueId == "character/xp" ? 1 : 2).ToArray();
        var result = base.ApplyEdits(ordered);
        if (!result.Succeeded)
        {
            Root = previous;
            return result;
        }
        var changes = _readers.Select(p => new ValueChange(p.Key, before[p.Key], p.Value())).Where(c => c.OldValue != c.NewValue).ToArray();
        return result with { Changes = changes, StructureChanged = changes.Length > 0 };
    }

    public override IReadOnlyList<FileOutput> Serialize()
    {
        var output = container.Encode(Root);
        var check = GearboxContainer.Parse(output, titleId, Profile).Decode();
        if (!check.Encode().AsSpan().SequenceEqual(Root.Encode()))
            throw new SaveFormatException("Gearbox output verification failed.");
        return [new FileOutput(relativePath, output)];
    }
}
