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
        var items = references.Catalog("xorjson-items");
        var world = references.Catalog("xorjson-world");
        var quests = Array(hero.Member("quest")?.Member("Quests"));
        return [
            Collection("attributes", "Stored attributes", "Hero", ["id", "value"], Array(hero.Member("Ability")?.Member("Attributes"))
                .Select(row => Row(row, ("id", Value(row.Member("AttributeName"))), ("value", Value(row.Member("CurrentValue")))))),
            Collection("inventory", "Inventory entries", "Inventory", ["name", "id", "category", "rarity", "slot", "target", "stack", "power", "effects"],
                Array(hero.Member("Inventory")?.Member("Entries")).Select(row => Inventory(row, items))),
            Collection("quests", "Stored quests", "Quests", ["name", "id", "state", "tasks", "focused"],
                Quests(quests, hero.Member("quest")?.Member("FocusedQuestId"), world)),
            Collection("tasks", "Stored quest tasks", "Quests", ["quest", "name", "id", "state", "progress"], Tasks(quests, world)),
            Collection("world", "World records", "World", ["group", "name", "id", "state", "count"], World(hero.Member("WorldExploration"), world)),
            Collection("achievements", "Stored achievements", "Collections", ["group", "name", "id", "state", "count"],
                Achievements(hero.Member("Achievements"), world)),
            Collection("collections", "Collected records", "Collections", ["group", "name", "id", "count"],
                Collected(hero.Member("CollectionsStats"), items)),
        ];
    }

    private static IEnumerable<CharacterRow> Quests(IReadOnlyList<JsonValueToken> quests, JsonValueToken? focused, IReferenceCatalog? catalog)
    {
        foreach (var row in quests)
        {
            var name = row.Member("QuestName");
            var focus = "Unknown";
            if (name?.Scalar.Kind == FieldValueKind.Text && focused?.Scalar.Kind == FieldValueKind.Text)
                focus = name.Scalar == focused.Scalar ? "true" : "false";
            yield return Row(row, ("name", Label(name, catalog)), ("id", Value(name)), ("state", Value(row.Member("State"))),
                ("tasks", Count(row.Member("TaskData"))),
                ("focused", Text(focus)));
        }
    }

    private static IEnumerable<CharacterRow> Tasks(IReadOnlyList<JsonValueToken> quests, IReferenceCatalog? catalog)
    {
        foreach (var quest in quests)
        foreach (var row in Array(quest.Member("TaskData")))
            yield return Row(row, ("quest", Text(Scalar(quest.Member("QuestName")))),
                ("name", Label(row.Member("TaskName"), catalog)), ("id", Value(row.Member("TaskName"))),
                ("state", Value(row.Member("State"))), ("progress", Value(row.Member("PartialProgress"))));
    }

    private static IEnumerable<CharacterRow> Collected(JsonValueToken? collections, IReferenceCatalog? catalog)
    {
        foreach (var group in Groups(collections))
        foreach (var row in Array(group))
        {
            var tag = row.IsContainer ? row.Member("Tag") : row;
            yield return Row(row, ("group", Text(group.Label)), ("name", Label(tag, catalog)), ("id", Value(tag)),
                ("count", Value(row.Member("Count"))));
        }
    }

    private static CharacterRow Inventory(JsonValueToken row, IReferenceCatalog? items)
    {
        var item = row.Member("ItemData");
        var tag = item?.Member("TypeTag");
        var record = items?.Find(Scalar(tag));
        return Row(row, ("name", Label(tag, items)), ("id", Value(tag)),
            ("category", new(record?.Category ?? "Unknown", Reference: record)),
            ("rarity", Value(item?.Member("RarityTag"))), ("slot", Value(row.Member("EquippedSlot"))),
            ("target", Value(item?.Member("TargetSlotOverride"))),
            ("stack", Value(row.Member("StackCount"))),
            ("power", Value(item?.Member("GeneratorData")?.Member("PowerGeneratorValues")?.Member("ItemPower"))),
            ("effects", Count(item?.Member("Effects"))));
    }

    private static IEnumerable<CharacterRow> World(JsonValueToken? world, IReferenceCatalog? catalog)
    {
        foreach (var group in Groups(world))
        {
            var entries = group.Label == "SavedFogOfWarExploration" ? Array(group.Member("Items")) : Array(group);
            foreach (var row in entries)
            {
                var tag = row.IsContainer ? row.Member("Tag") ?? row.Member("DoorId") ?? row.Member("SoftObjectPath") : row;
                yield return Row(row, ("group", Text(group.Label)), ("name", Label(tag, catalog)), ("id", Value(tag)),
                    ("state", Value(row.Member("CurrentState"))), ("count", Count(row.Member("Data"))));
            }
        }
    }

    private static IEnumerable<CharacterRow> Achievements(JsonValueToken? achievements, IReferenceCatalog? catalog)
    {
        foreach (var group in Groups(achievements))
        foreach (var row in Groups(group))
        {
            var record = catalog?.Find(row.Label);
            yield return Row(row, ("group", Text(group.Label)), ("name", new(record?.Name ?? row.Label, Reference: record)),
                ("id", Text(row.Label)), ("state", Value(row.Member("bCompleted"))),
                ("count", row.Member("Count") is { } count ? Value(count) : Count(row.Member("CollectedTags"))));
        }
    }

    private static CharacterCollection Collection(string id, string title, string tab, string[] keys, IEnumerable<CharacterRow> rows) =>
        new("character/" + id, title, tab, keys.Append("details").Select(key => new QueryableColumn(key, key switch
        {
            "id" => "Raw identifier", "name" => "Catalog label (Inferred)", "details" => "Raw details",
            _ => char.ToUpperInvariant(key[0]) + key[1..],
        })).ToArray(), rows.ToArray());

    private static CharacterRow Row(JsonValueToken source, params (string Key, CharacterCell Cell)[] cells) =>
        new(source, cells.ToDictionary(cell => cell.Key, cell => cell.Cell, StringComparer.Ordinal));

    private static CharacterCell Value(JsonValueToken? token) => new(Scalar(token), token is { IsContainer: false } ? token : null);
    private static CharacterCell Text(string text) => new(text);
    private static CharacterCell Count(JsonValueToken? token) => new(token?.Kind == JsonTokenType.StartArray
        ? token.Children.Count.ToString(CultureInfo.InvariantCulture) : "Unknown");
    private static CharacterCell Label(JsonValueToken? token, IReferenceCatalog? catalog)
    {
        var id = Scalar(token);
        var record = catalog?.Find(id);
        return new(record?.Name ?? id, Reference: record);
    }
    private static string Scalar(JsonValueToken? token) => token is { IsContainer: false } ? token.Scalar.ToString() : "";
    private static IReadOnlyList<JsonValueToken> Array(JsonValueToken? token) => token?.Kind == JsonTokenType.StartArray ? token.Children : [];
    private static IReadOnlyList<JsonValueToken> Groups(JsonValueToken? token) => token?.Kind == JsonTokenType.StartObject ? token.Children : [];
}
