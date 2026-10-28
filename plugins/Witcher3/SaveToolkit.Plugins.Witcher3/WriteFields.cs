using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.Witcher3;

internal delegate FieldNode FieldBinding(string id, string label, FieldSpec spec, Func<FieldValue> get,
    Action<FieldValue>? set = null, Func<FieldValue, string?>? validate = null);

/// <summary>Curated writes retain source evidence and gameplay caveats at each editing control.</summary>
internal sealed class WriteFields(FieldBinding bind)
{
    public static string IdentityId(string array, string id) => $"abilities/{array}/{id}/identity";
    public FieldNode Identity(string array, AbilityEntry entry, ReferenceRecord? reference = null) => bind(IdentityId(array, entry.Id), "Saved identity",
        array is "skills" or "mutations" ? new ReferenceSpec("abilities") { AllowCustom = true } : new TextSpec(), () => FieldValue.Of(entry.Id)) with {
        Confidence = Confidence.Unknown, Description = "Source: native saved ability definition. Browse the ability catalog without changing this saved identity. " +
            "Identity does not establish acquisition or availability. " +
            reference?.AttributionText,
    };
    private const string ConverterSource = "Source: cahian, MIT, https://github.com/cahian/witcher3-remastered-to-nextgen-save-converter/tree/7ae8b6fe71c4b36e571246ad25629d03c7ec8219/w3save . ";
    private const string GwentSource = "Source: Vishal Gupta / rfvgyhn, MIT, https://github.com/vishalgupta-git/GwentTracker/blob/d8eab8c25c409be42a0ea7b43b901a43dfefed18/src/lib/save/parse.ts . ";
    public const string ConsoleWarning = "Not validated on console. The game may reject the edited save or rebuild dependent state.";
    public const string DurabilityWarning = ConsoleWarning + " Float32 at native item +19; 0..100 is a conservative range, not a proven PS5 limit. " +
        "Repair changes stored durability only; Crowns and repair achievements are not changed. The -1 sentinel is preserved.";
    public const string SkillWarning = ConsoleWarning + " Changes the stored rank only. Costs, path spending, prerequisites, " +
        "equipped slots, modifiers and cached stats are not recalculated; this is not an in-game purchase or respec.";
    public const string GwentWarning = ConsoleWarning + " Changes existing collection counts only. Decks, rewards, quests and achievements " +
        "are not synchronized; decreasing a count may invalidate a deck. Limits use observed copies, not a proven legal cap.";

    public FieldNode Durability(InventoryItem item) => bind(item.DurabilityId, "Stored durability",
        item.CanEditDurability ? FloatSpec.Single with { Min = 0, Max = InventoryItem.MaximumDurability } : FloatSpec.Single,
        () => FieldValue.Of(item.Durability),
        item.CanEditDurability ? v => item.Durability = (float)v.AsDouble() : null,
        v => v.Kind is not (FieldValueKind.Float or FieldValueKind.Integer) || !double.IsFinite(v.AsDouble()) ||
            v.AsDouble() < 0 || v.AsDouble() > InventoryItem.MaximumDurability ? "Durability must be finite and in 0..100." : null) with {
        Confidence = Confidence.Inferred,
        Description = $"Native inventory instance {item.Id}, float32 at item +19. Based on bounded PS5 sample records. " + ConverterSource + ConsoleWarning,
        Warning = DurabilityWarning,
    };

    public FieldNode Rank(SkillRank rank) => bind(rank.ValueId, $"{rank.Entry.Id} · Stored rank", new IntegerSpec(0, rank.Maximum),
        () => FieldValue.Of(rank.Value), v => rank.Value = v.AsInt32(), v => IntegerError(v, rank.Maximum)) with {
        Confidence = Confidence.Inferred,
        Description = "Player-owned SSkill.level; omitted zero is inserted using the saved Int32 name/type. " +
            "Bounded to the record's own maxLevel, not a cross-build wiki limit. " + ConverterSource + ConsoleWarning,
        Warning = SkillWarning,
    };

    public FieldNode Copies(GwentCopies copies) => bind(copies.ValueId, $"Card {copies.Card.Index} · Saved copies",
        new IntegerSpec(0, copies.Maximum), () => FieldValue.Of(copies.Value), v => copies.Value = v.AsInt32(),
        v => IntegerError(v, copies.Maximum)) with {
        Confidence = Confidence.Inferred,
        Description = "Existing CR4GwintManager cardIndex/numCopies Int32 pair before the deck boundary. " +
            "Based on all eight PS5 records. " + GwentSource + ConsoleWarning,
        Warning = GwentWarning,
    };

    private static string? IntegerError(FieldValue value, int maximum) =>
        value.Kind != FieldValueKind.Integer || value.AsInteger() < 0 || value.AsInteger() > maximum
            ? $"Choose a whole value in 0..{maximum}." : null;
}
