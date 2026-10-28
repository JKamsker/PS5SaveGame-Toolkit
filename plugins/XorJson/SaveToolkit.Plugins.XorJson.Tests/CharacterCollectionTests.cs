using System.Text;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class CharacterCollectionTests
{
    [Fact]
    public void SampleTablesExposeStoredValuesAndAttributedLabelsWithoutChangingBytes()
    {
        var plugin = new XorJsonPlugin();
        var file = SampleSaves.Load("PPSA16064").AllDiskFiles.Single(file => !file.IsSystemFile && file.Directory.Name.StartsWith("Character", StringComparison.Ordinal));
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA16064");
        using var document = plugin.Open(file, references);
        using var session = new DocumentSession("character-tables", file, plugin, document, references: references);
        var tabs = Assert.IsType<TabsNode>(session.BuildView());
        var tables = tabs.Tabs.SelectMany(tab => tab.Children).OfType<QueryableTableNode>().ToArray();
        Assert.Equal(7, tables.Length);
        Assert.All(tables, table => Assert.Contains("read-only", table.Description!, StringComparison.OrdinalIgnoreCase));
        var attributes = session.QueryCollection("character/attributes", new());
        Assert.Equal(9, attributes.TotalCount);
        var emeralds = Assert.Single(attributes.Rows, row => CellText(row.Cells["id"]) == "Emeralds");
        Assert.Equal("0", Assert.IsType<FieldNode>(emeralds.Cells["value"]).Value.AsExactNumber());
        var inventory = session.QueryCollection("character/inventory", new());
        Assert.Equal(5, inventory.TotalCount);
        Assert.All(inventory.Rows, row =>
        {
            Assert.Contains("MIT", Assert.IsType<SectionNode>(row.Cells["name"]).Description!);
            var id = Assert.IsType<FieldNode>(row.Cells["id"]);
            Assert.IsType<ReferenceSpec>(id.Spec);
            Assert.True(id.Spec.ReadOnly);
            Assert.Equal(Confidence.Inferred, id.Confidence);
            Assert.False(session.ApplyEdits([new(id.ValueId, JsonSerializer.SerializeToElement("SW.Item.Bow"))]).Ok);
            var details = Assert.IsType<SectionNode>(row.Cells["details"]);
            Assert.NotEmpty(session.Expand(details.LazyId!));
        });
        var quests = session.QueryCollection("character/quests", new() { Filters = new Dictionary<string, string> { ["focused"] = "true" } });
        Assert.Equal(49, quests.TotalCount);
        var focused = Assert.Single(quests.Rows);
        Assert.Equal("CA00", CellText(focused.Cells["id"]));
        Assert.Equal("Escape from Squid Coast", Assert.IsType<SectionNode>(focused.Cells["name"]).Title);
        var tasks = session.QueryCollection("character/tasks", new() { Text = "Get back to camp" });
        Assert.Contains(tasks.Rows, row => CellText(row.Cells["id"]) == "CA00_E01");
        var fog = Assert.Single(session.QueryCollection("character/world", new()
        {
            Filters = new Dictionary<string, string> { ["group"] = "SavedFogOfWarExploration" },
        }).Rows);
        Assert.Equal("SW.Region.Camp", CellText(fog.Cells["id"]));
        Assert.Equal("256", CellText(fog.Cells["count"]));
        Assert.Equal(32, session.QueryCollection("character/achievements", new()).TotalCount);
        Assert.Equal(3, session.QueryCollection("character/collections", new()).TotalCount);
        foreach (var collection in session.QueryableCollections)
        {
            var page = session.QueryCollection(collection.Id, new() { Limit = 200 });
            Assert.Equal(page.Rows.Count, page.Rows.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count());
            Assert.All(page.Rows.SelectMany(row => row.Cells.Values).OfType<FieldNode>(), field => Assert.True(field.Spec.ReadOnly));
            _ = JsonSerializer.Serialize(page);
        }
        Assert.False(session.State.IsDirty);
        Assert.Equal(file.ReadAllBytes(), document.Serialize()[0].Content.ToArray());
    }

    [Fact]
    public void LargeCollectionsSearchBeyondLoadedPagesAndKeepDeterministicSessionKeys()
    {
        var rows = Enumerable.Range(0, 2001).Select(index => new { AttributeName = $"Raw{index:D4}", CurrentValue = index });
        using var document = OpenHero(new { Ability = new { Attributes = rows } });
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        Assert.Equal(2001, queryable.Collections.Single(collection => collection.Id == "character/attributes").TotalCount);
        var first = queryable.Query("character/attributes", new() { Limit = 2000 });
        Assert.Equal(200, first.Rows.Count);
        var next = queryable.Query("character/attributes", new() { Offset = 200, Limit = 200 });
        Assert.Empty(first.Rows.Select(row => row.Key).Intersect(next.Rows.Select(row => row.Key)));
        var last = Assert.Single(queryable.Query("character/attributes", new() { Text = "Raw2000" }).Rows);
        Assert.Equal("2000", Assert.IsType<FieldNode>(last.Cells["value"]).Value.AsExactNumber());
        Assert.Equal(last.Key, queryable.Query("character/attributes", new() { SortColumn = "id", Descending = true, Limit = 1 }).Rows[0].Key);
        _ = document.BuildView();
        Assert.Equal(last.Key, Assert.Single(queryable.Query("character/attributes", new() { Text = "Raw2000" }).Rows).Key);
        Assert.Empty(queryable.Query("character/attributes", new() { Offset = int.MaxValue }).Rows);
        Assert.Throws<KeyNotFoundException>(() => queryable.Query("missing", new()));
        foreach (var invalid in new QueryableCollectionQuery[]
        {
            new() { Cursor = "opaque" }, new() { SortColumn = "absent" }, new() { Text = new string('x', 257) },
            new() { Filters = new Dictionary<string, string> { ["absent"] = "x" } },
            new() { Filters = new Dictionary<string, string> { ["id"] = new string('x', 257) } },
        }) Assert.Throws<ArgumentException>(() => queryable.Query("character/attributes", invalid));
    }

    [Fact]
    public void DuplicateIdsUnknownCatalogEntriesAndMalformedShapesRemainDistinctAndLossless()
    {
        const string text = "{\"SerializeMeta\":{\"HardFormat\":\"FCharacterSaveV1\"},\"CharacterSaveV1\":{\"Inventory\":{\"Entries\":[" +
            "{\"ItemData\":{\"TypeTag\":\"SW.Item.Future\",\"TypeTag\":\"SW.Item.Sword\"},\"StackCount\":1}," +
            "{\"ItemData\":{\"TypeTag\":\"SW.Item.Future\"},\"StackCount\":2}]}," +
            "\"quest\":{\"Quests\":[{\"QuestName\":\"Future\",\"State\":\"NewState\"},{\"QuestName\":\"Future\",\"State\":\"NewState\"}]}," +
            "\"Ability\":{\"Attributes\":{\"FutureShape\":[1]}},\"FutureUnknown\":true}}";
        var bytes = Encoding.UTF8.GetBytes(text);
        using var document = new XorJsonPlugin().Open(MemorySaveSet.SingleFile("hero.sav", bytes));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var inventory = queryable.Query("character/inventory", new());
        Assert.Equal(2, inventory.TotalCount);
        Assert.Equal("", CellText(inventory.Rows[0].Cells["id"])); // Duplicate semantic keys are not guessed.
        Assert.Equal("SW.Item.Future", CellText(inventory.Rows[1].Cells["id"]));
        var quests = queryable.Query("character/quests", new() { SortColumn = "id" });
        var reverse = queryable.Query("character/quests", new() { SortColumn = "id", Descending = true });
        Assert.Equal(2, quests.Rows.Count);
        Assert.All(quests.Rows, row => Assert.Equal("Unknown", CellText(row.Cells["focused"])));
        Assert.Equal(2, quests.Rows.Select(row => row.Key).Distinct().Count());
        Assert.Equal(quests.Rows.Select(row => row.Key), reverse.Rows.Select(row => row.Key)); // Same tie-break in either direction.
        Assert.Equal(0, queryable.Query("character/attributes", new()).TotalCount);
        Assert.Contains(XorJsonTests.Fields(document), field => field.Label == "FutureShape" || field.Label == "FutureUnknown");
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
    }

    private static ISaveDocument OpenHero(object hero) => new XorJsonPlugin().Open(MemorySaveSet.SingleFile("hero.sav",
        JsonSerializer.SerializeToUtf8Bytes(new { SerializeMeta = new { HardFormat = "FCharacterSaveV1" }, CharacterSaveV1 = hero })));

    private static string CellText(ViewNode node) => node switch
    {
        FieldNode field => field.Value.ToString(), TextNode text => text.Text,
        _ => throw new InvalidOperationException("Expected a scalar cell."),
    };
}
