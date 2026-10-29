using System.Globalization;
using System.Text.Json;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.XorJson;

/// <summary>Observed PS5 paths; unknown and ambiguous shapes remain available in raw details.</summary>
internal static class CharacterCollections
{
    public static IReadOnlyList<CharacterCollection> Build(JsonValueToken root, IReferenceContext references)
    {
        var hero = root.Member("CharacterSaveV1")!;
        var attributes = references.Catalog("xorjson-attributes");
        var items = references.Catalog("xorjson-items");
        var world = references.Catalog("xorjson-world");
        var quests = Array(hero.Member("quest")?.Member("Quests"));
        var focused = hero.Member("quest")?.Member("FocusedQuestId");
        return [
            Collection("attributes", "Stored attributes", "Hero", ["id", "value"], Array(hero.Member("Ability")?.Member("Attributes")),
                row => Row(row, ("id", Value(row.Member("AttributeName"), attributes)), ("value", Value(row.Member("CurrentValue"))))),
            Collection("inventory", "Inventory entries", "Inventory", ["icon", "name", "id", "category", "rarity", "slot", "target", "stack", "power", "effects"],
                Array(hero.Member("Inventory")?.Member("Entries")), row => Inventory(row, items)),
            Collection("quests", "Stored quests", "Quests", ["name", "id", "state", "tasks", "focused"],
                quests, row => Quest(row, focused, world)),
            Collection("tasks", "Stored quest tasks", "Quests", ["quest", "name", "id", "state", "progress"],
                quests.SelectMany(quest => Array(quest.Member("TaskData"))).ToArray(), row => Task(row, world)),
            Collection("world", "World records", "World", ["group", "name", "id", "state", "count"],
                Groups(hero.Member("WorldExploration")).SelectMany(group =>
                    group.Label == "SavedFogOfWarExploration" ? Array(group.Member("Items")) : Array(group)).ToArray(), row => World(row, world)),
            Collection("achievements", "Stored achievements", "Collections", ["group", "name", "id", "state", "count"],
                Groups(hero.Member("Achievements")).SelectMany(Groups).ToArray(), row => Achievement(row, world)),
            Collection("collections", "Collected records", "Collections", ["group", "icon", "name", "id", "count"],
                Groups(hero.Member("CollectionsStats")).SelectMany(Array).ToArray(), row => Collected(row, items)),
        ];
    }

    private static CharacterRow Quest(JsonValueToken row, JsonValueToken? focused, IReferenceCatalog? catalog)
    {
        var name = row.Member("QuestName");
        var focus = "Unknown";
        if (name?.Scalar.Kind == FieldValueKind.Text && focused?.Scalar.Kind == FieldValueKind.Text)
            focus = name.Scalar == focused.Scalar ? "true" : "false";
        return Row(row, ("name", Label(name, catalog)), ("id", Value(name)), ("state", Value(row.Member("State"))),
            ("tasks", Count(row.Member("TaskData"))), ("focused", Text(focus)));
    }

    private static CharacterRow Task(JsonValueToken row, IReferenceCatalog? catalog) =>
        Row(row, ("quest", Text(Scalar(row.Parent?.Parent?.Member("QuestName")))),
            ("name", Label(row.Member("TaskName"), catalog)), ("id", Value(row.Member("TaskName"))),
            ("state", Value(row.Member("State"))), ("progress", Value(row.Member("PartialProgress"))));

    private static CharacterRow Collected(JsonValueToken row, IReferenceCatalog? catalog)
    {
        var tag = row.IsContainer ? row.Member("Tag") : row;
        var record = catalog?.Find(Scalar(tag));
        return Row(row, ("group", Text(row.Parent!.Label)), ("icon", Icon(record)),
            ("name", Label(tag, record)), ("id", Value(tag)), ("count", Value(row.Member("Count"))));
    }

    private static CharacterRow Inventory(JsonValueToken row, IReferenceCatalog? items)
    {
        var item = row.Member("ItemData");
        var tag = item?.Member("TypeTag");
        var record = items?.Find(Scalar(tag));
        return Row(row, ("icon", Icon(record)), ("name", Label(tag, record)), ("id", Value(tag)),
            ("category", new(record?.Category ?? "Unknown", Reference: record)),
            ("rarity", Value(item?.Member("RarityTag"))), ("slot", Value(row.Member("EquippedSlot"))),
            ("target", Value(item?.Member("TargetSlotOverride"))), ("stack", Value(row.Member("StackCount"))),
            ("power", Value(item?.Member("GeneratorData")?.Member("PowerGeneratorValues")?.Member("ItemPower"))),
            ("effects", Count(item?.Member("Effects"))));
    }

    private static CharacterRow World(JsonValueToken row, IReferenceCatalog? catalog)
    {
        var group = row.Parent!.Parent?.Label == "SavedFogOfWarExploration" ? row.Parent.Parent : row.Parent;
        var tag = row.IsContainer ? row.Member("Tag") ?? row.Member("DoorId") ?? row.Member("SoftObjectPath") : row;
        return Row(row, ("group", Text(group.Label)), ("name", Label(tag, catalog)), ("id", Value(tag)),
            ("state", Value(row.Member("CurrentState"))), ("count", Count(row.Member("Data"))));
    }

    private static CharacterRow Achievement(JsonValueToken row, IReferenceCatalog? catalog)
    {
        var record = catalog?.Find(row.Label);
        return Row(row, ("group", Text(row.Parent!.Label)), ("name", new(record?.Name ?? row.Label, Reference: record)),
            ("id", Text(row.Label)), ("state", Value(row.Member("bCompleted"))),
            ("count", row.Member("Count") is { } count ? Value(count) : Count(row.Member("CollectedTags"))));
    }

    private static CharacterCollection Collection(string id, string title, string tab, string[] keys,
        IReadOnlyList<JsonValueToken> sources, Func<JsonValueToken, CharacterRow> project) =>
        new("character/" + id, title, tab, keys.Append("details").Select(key => new QueryableColumn(key, key switch
        {
            "id" => "Raw identifier", "name" => "Catalog label (Inferred)", "details" => "Raw details",
            _ => char.ToUpperInvariant(key[0]) + key[1..],
        })).ToArray(), sources, project);

    private static CharacterRow Row(JsonValueToken source, params (string Key, CharacterCell Cell)[] cells) =>
        new(source, cells.ToDictionary(cell => cell.Key, cell => cell.Cell, StringComparer.Ordinal));

    private static CharacterCell Value(JsonValueToken? token, IReferenceCatalog? catalog = null) =>
        new(Scalar(token), token is { IsContainer: false } ? token : null,
            token?.Scalar.Kind == FieldValueKind.Text ? catalog?.Find(Scalar(token)) : null);
    private static CharacterCell Text(string text) => new(text);
    private static CharacterCell Icon(ReferenceRecord? record) => new("", Reference: record);
    private static CharacterCell Count(JsonValueToken? token) => new(token?.Kind == JsonTokenType.StartArray
        ? token.Children.Count.ToString(CultureInfo.InvariantCulture) : "Unknown");
    private static CharacterCell Label(JsonValueToken? token, IReferenceCatalog? catalog) => Label(token, catalog?.Find(Scalar(token)));
    private static CharacterCell Label(JsonValueToken? token, ReferenceRecord? record) => new(record?.Name ?? Scalar(token), Reference: record);
    private static string Scalar(JsonValueToken? token) => token is { IsContainer: false } ? token.Scalar.ToString() : "";
    private static IReadOnlyList<JsonValueToken> Array(JsonValueToken? token) => token?.Kind == JsonTokenType.StartArray ? token.Children : [];
    private static IReadOnlyList<JsonValueToken> Groups(JsonValueToken? token) => token?.Kind == JsonTokenType.StartObject ? token.Children : [];
}
