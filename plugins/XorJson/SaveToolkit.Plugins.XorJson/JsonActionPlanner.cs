using System.Globalization;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson;

/// <summary>Pure plans over token identities. Never registers bindings or mutates views while previewing.</summary>
internal sealed class JsonActionPlanner(JsonEditModel model, IReferenceContext references, Func<bool> canEdit)
{
    private const string NonStackableSentinel = "-1";
    private static readonly ExactNumberSpec NonStackableCount = JsonTokenTree.NumberSpec with
    { Min = NonStackableSentinel, Max = NonStackableSentinel };

    public IEnumerable<ActionNode> Actions(bool character)
    {
        yield return Action(JsonActions.Bulk, "Set selected scalar values",
            "Applies one literal to scalar row keys; object rows are skipped. Use a character action for inventory rows.",
            [Parameter("value", "JSON scalar", new TextSpec { MaxLength = JsonTokenTree.MaxBytes }, FieldValue.Of("null"))]);
        if (!character) yield break;
        yield return Action(JsonActions.Attribute, "Set stored attribute",
            "Sets existing matching attribute rows only. PC limits are inferred; level/XP and reward dependencies are not adjusted.",
            [Parameter("id", "Attribute", Picker("xorjson-attributes"), FieldValue.Of("Emeralds")),
                Parameter("value", "Stored value", new ExactNumberSpec { Min = "0", Max = "2147483647" }, FieldValue.ExactNumber("0"))]);
        yield return Action(JsonActions.Stacks, "Set selected inventory stacks",
            "Sets existing stack tokens. Merchant stock, cosmetics and non-stackable or malformed records are skipped. PC ceiling 9,999 is unverified on PS5.",
            [Parameter("value", "Stack count", new ExactNumberSpec { Min = "1", Max = "9999" }, FieldValue.ExactNumber("1"))]);
        yield return Action(JsonActions.Item, "Replace selected item identifiers",
            "Replaces existing TypeTags within the same catalog category. Effects, rarity, power and slots are retained, and may be incompatible; no item is synthesized.",
            [Parameter("id", "Replacement item", Picker("xorjson-items"), FieldValue.Of("SW.Item.Sword"))]);
        yield return Action(JsonActions.Rarity, "Set selected item rarity",
            "Changes only existing rarity tags. Unique effects are not created; effect counts and game acceptance remain unknown.",
            [Parameter("id", "Rarity", Picker("xorjson-rarities"), FieldValue.Of("SW.Rarity.Common"))]);
    }

    public ActionPlan Plan(string id, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        if (!JsonActions.IsPlanned(id)) return Error(id, "Unknown planned action.");
        if (!canEdit()) return Error(id, "This character version or online mode is read-only.");
        var collection = scope.QueryScope?.CollectionId;
        if (scope.QueryScope is not null && scope.RowKeys is null)
            return Error(id, "Query selections must be resolved by Core before planning.");
        if (collection is not null && !(id switch
        {
            JsonActions.Attribute => collection == "character/attributes",
            JsonActions.Stacks or JsonActions.Item or JsonActions.Rarity => collection == "character/inventory",
            _ => collection.StartsWith("json/array/", StringComparison.Ordinal),
        })) return Error(id, "This action does not accept the selected collection.");
        try
        {
            var value = Value(id, parameters);
            var array = CandidateArray(id);
            var selected = scope.RowKeys ?? (id == JsonActions.Bulk ? model.Nodes.Where(n => !n.IsContainer)
                : array?.Children ?? []).Select(n => n.ValueId);
            var changes = new List<PlannedChange>();
            var skipped = new List<SkippedActionRow>();
            foreach (var key in selected.Distinct(StringComparer.Ordinal))
            {
                if (!JsonActions.TryFind(model, key, out var row) || row.ValueId != key)
                    return Error(key, $"Unknown or malformed row key '{key}'.");
                if (id == JsonActions.Bulk ? row.IsContainer : array is null || row.Parent != array)
                { skipped.Add(new(key, "Unknown or incompatible row for this action.")); continue; }
                var target = Target(id, row, parameters);
                var reason = Eligible(id, row, value) ?? (target is null || target.IsContainer ? "Required existing scalar is absent, ambiguous or does not match the attribute." : null);
                if (reason is null) reason = JsonActions.Validate(target!, value, references);
                if (reason is not null) { skipped.Add(new(key, reason)); continue; }
                if (target!.Current != value) changes.Add(new(target.ValueId, target.TypedPath, target.Current, value));
                if (changes.Count + skipped.Count > JsonActions.MaxChanges) return Error(id, "Preview exceeds 5,000 rows; narrow the selection.");
            }
            return new ActionPlan { Changes = changes, SkippedRows = skipped,
                Warnings = [JsonSaveDocument.WriteWarning, "Only the listed stored tokens change. PC limits and labels are inferred; gameplay dependencies, rewards and effect compatibility are not repaired."] };
        }
        catch (Exception ex) when (ex is SaveFormatException or ArgumentException)
        {
            return Error(id, ex.Message);
        }
    }

    private JsonValueToken? CandidateArray(string id)
    {
        if (id == JsonActions.Bulk) return null;
        var hero = model.Root.Member("CharacterSaveV1");
        var array = id == JsonActions.Attribute ? hero?.Member("Ability")?.Member("Attributes") : hero?.Member("Inventory")?.Member("Entries");
        return array?.Kind == System.Text.Json.JsonTokenType.StartArray ? array : null;
    }

    private FieldValue Value(string id, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        var key = id is JsonActions.Item or JsonActions.Rarity ? "id" : "value";
        if (!parameters.TryGetValue(key, out var value)) throw new SaveFormatException("Missing action parameter.");
        if (id == JsonActions.Bulk)
        {
            if (value.Kind != FieldValueKind.Text) throw new SaveFormatException("Provide one JSON scalar literal.");
            var scalar = JsonTokenTree.Parse(Encoding.UTF8.GetBytes(value.AsString())).Nodes[0];
            if (scalar.IsContainer) throw new SaveFormatException("Bulk writes accept a scalar, not an object or array.");
            if (scalar.Spec?.ReadOnly == true)
                throw new SaveFormatException("The JSON scalar exceeds editable exact-number limits.");
            return scalar.Current;
        }
        if (id is JsonActions.Item or JsonActions.Rarity)
        {
            var catalog = id == JsonActions.Item ? "xorjson-items" : "xorjson-rarities";
            if (value.Kind != FieldValueKind.Text || references.Catalog(catalog)?.Find(value.AsString()) is null)
                throw new SaveFormatException("Choose a replacement from the attributed catalog.");
            return value;
        }
        var bounds = id == JsonActions.Stacks ? (Min: "1", Max: "9999", Whole: true) : AttributeBounds(parameters);
        if (value.Kind != FieldValueKind.ExactNumber || ExactJsonNumber.Validate(value.AsExactNumber(),
            new ExactNumberSpec { Min = bounds.Min, Max = bounds.Max }) is not null)
            throw new SaveFormatException("Value exceeds the inferred PC bounds for this action.");
        if (bounds.Whole && !int.TryParse(value.AsExactNumber(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            throw new SaveFormatException("This action requires a whole number written without exponent notation.");
        return value;
    }

    private (string Min, string Max, bool Whole) AttributeBounds(IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (!parameters.TryGetValue("id", out var value) || value.Kind != FieldValueKind.Text ||
            references.Catalog("xorjson-attributes")?.Find(value.AsString()) is not { } record)
            throw new SaveFormatException("Choose an attribute from the attributed catalog.");
        return (record.Attributes["inferredMinimum"], record.Attributes["inferredMaximum"], record.Key != "XP");
    }

    private static JsonValueToken? Target(string id, JsonValueToken row, IReadOnlyDictionary<string, FieldValue> parameters) => id switch
    {
        JsonActions.Bulk => row,
        JsonActions.Attribute => row.Member("AttributeName")?.Scalar == parameters["id"] ? row.Member("CurrentValue") : null,
        JsonActions.Stacks => row.Member("StackCount"),
        JsonActions.Item => row.Member("ItemData")?.Member("TypeTag"),
        JsonActions.Rarity => row.Member("ItemData")?.Member("RarityTag"),
        _ => null,
    };

    private string? Eligible(string id, JsonValueToken row, FieldValue replacement)
    {
        if (id is JsonActions.Bulk or JsonActions.Attribute) return null;
        var item = row.Member("ItemData");
        var type = item?.Member("TypeTag")?.Scalar;
        if (type is not { Kind: FieldValueKind.Text }) return "Missing or ambiguous item identifier.";
        var record = references.Catalog("xorjson-items")?.Find(type.Value.AsString());
        var slot = item?.Member("TargetSlotOverride")?.Scalar;
        if (slot is { Kind: FieldValueKind.Text } && slot.Value.AsString() is not ("None" or ""))
            return "Target-slot override (including possible merchant stock) is retained and skipped.";
        if (record is null || record.Category is "Cosmetic" or "QuestItem" or "Currency") return "Unknown or special-purpose item is skipped.";
        if (id == JsonActions.Stacks && row.Member("StackCount")?.Scalar is { Kind: FieldValueKind.ExactNumber } count &&
            ExactJsonNumber.Validate(count.AsExactNumber(), NonStackableCount) is null)
            return "Non-stackable sentinel is retained.";
        return id == JsonActions.Item && references.Catalog("xorjson-items")?.Find(replacement.AsString())?.Category != record.Category
            ? "Replacement category differs; use the individual raw editor after reviewing dependencies." : null;
    }

    private FieldSpec Picker(string catalog) => references.Catalog(catalog) is null ? new TextSpec() : new ReferenceSpec(catalog);
    private static FieldNode Parameter(string id, string label, FieldSpec spec, FieldValue value) => new(id, label, spec, value);
    private static ActionNode Action(string id, string label, string description, IReadOnlyList<FieldNode> parameters) => new(id, label)
    { SupportsPlanning = true, Tone = Tone.Warning, Confirm = JsonSaveDocument.WriteWarning, Description = description, Parameters = parameters };
    private static ActionPlan Error(string id, string message) => new() { Errors = [new(id, message)] };
}
