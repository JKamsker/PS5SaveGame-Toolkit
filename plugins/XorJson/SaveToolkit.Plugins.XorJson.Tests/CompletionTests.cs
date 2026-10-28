using System.Text;
using System.Text.Json;
using System.IO.Compression;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class CompletionTests
{
    [Fact]
    public void F12HookUsesProductionParserAndBoundsOutput()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"rows\":[{\"name\":\"first\",\"value\":1},{\"name\":\"second\",\"value\":2}]}").Select(byteValue => unchecked((byte)(byteValue - 1))).ToArray();
        var file = MemorySaveSet.SingleFile("data.sav", bytes);
        var hook = new XorJsonPlugin().Decoders.Single();
        var tree = new HookSaveDecoder(hook).Decode(file, new DecodeLimits { MaxValues = 3 });
        Assert.True(tree.Truncated);
        Assert.Contains(tree.Values, value => value.Path.Contains("name=first", StringComparison.Ordinal));
        Assert.Contains(tree.Warnings, warning => warning.Contains("limit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LargeRawArrayPublishesQueryableCollectionAndKeepsRowsStable()
    {
        var values = string.Join(',', Enumerable.Range(0, 150));
        var bytes = Encoding.UTF8.GetBytes("[" + values + "]").Select(byteValue => unchecked((byte)(byteValue - 1))).ToArray();
        using var document = new XorJsonPlugin().Open(MemorySaveSet.SingleFile("large.sav", bytes));
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var collection = Assert.Single(query.Collections, item => item.Id.StartsWith("json/array/", StringComparison.Ordinal));
        var page = query.Query(collection.Id, new QueryableCollectionQuery { Offset = 100, Limit = 50 });
        Assert.Equal(150, page.TotalCount);
        Assert.Equal(50, page.Rows.Count);
        Assert.Equal("[100]", page.Rows[0].Cells["id"] is TextNode text ? text.Text : "");
        Assert.Equal(page.Rows.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count(), page.Rows.Count);
    }

    [Fact]
    public void CharacterPlannedActionsUseCatalogBoundsAndSkipMerchantRows()
    {
        var plugin = new XorJsonPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA16064");
        using var document = plugin.Open(MemorySaveSet.SingleFile("hero.sav", Encoding.UTF8.GetBytes(CharacterJson())), references);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var rows = query.Query("character/attributes", new QueryableCollectionQuery { Limit = 10 }).Rows;
        var plan = planner.PlanAction("character/set-attribute",
            new Dictionary<string, FieldValue> { ["id"] = FieldValue.Of("Emeralds"), ["value"] = FieldValue.ExactNumber("99999") },
            new ActionScope(rows.Select(row => row.Key).ToArray()));
        Assert.Empty(plan.Errors);
        Assert.Single(plan.Changes);
        Assert.Contains("dependencies", plan.Warnings.Single(warning => warning.Contains("dependencies", StringComparison.Ordinal)));
        var inventory = query.Query("character/inventory", new QueryableCollectionQuery { Limit = 10 });
        var stackPlan = planner.PlanAction("character/set-stacks",
            new Dictionary<string, FieldValue> { ["value"] = FieldValue.ExactNumber("9999") },
            new ActionScope(inventory.Rows.Select(row => row.Key).ToArray()));
        Assert.Single(stackPlan.Changes);
        Assert.Contains(stackPlan.SkippedRows, row => row.Reason.Contains("merchant", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void F6QueryScopeResolvesUnloadedInventoryRowsAndKeepsOneUndoStep()
    {
        var plugin = new XorJsonPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA16064");
        var root = Path.Combine(Path.GetTempPath(), "SaveToolkit.XorJson.Completion", Guid.NewGuid().ToString("N"));
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new WorkspaceStoreOptions { DataRoot = root });
        var zipBytes = new MemoryStream();
        using (var zip = new ZipArchive(zipBytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("PPSA16064/sdimg_SAVE/hero.sav").Open();
            var bytes = Encoding.UTF8.GetBytes(CharacterJson());
            entry.Write(bytes);
        }
        var workspace = store.Create("completion", [new ImportSource("completion.zip", () => new MemoryStream(zipBytes.ToArray()))]);
        var file = workspace.Set.AllDiskFiles.Single();
        using var document = plugin.Open(file, references);
        using var session = new DocumentSession("completion", file, plugin, document, references: references);
        _ = session.BuildView();
        var scope = new ActionScope
        {
            QueryScope = new ActionQueryScope("character/inventory", new QueryableCollectionQuery { Text = "Sword", Limit = 50 }, session.State.Revision),
        };
        var preview = session.PlanAction("character/set-stacks", new Dictionary<string, JsonElement>
        {
            ["value"] = JsonSerializer.SerializeToElement(2),
        }, scope);
        Assert.True(preview.PlanId is not null, string.Join("; ", preview.Plan.Errors.Select(error => error.Message)));
        Assert.Single(preview.Plan.Changes);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.True(session.State.CanUndo);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
    }

    [Fact]
    public void QuestGraphAndUnpositionedMapHaveTextAlternativesAndStableTargets()
    {
        var plugin = new XorJsonPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA16064");
        using var document = plugin.Open(MemorySaveSet.SingleFile("hero.sav", Encoding.UTF8.GetBytes(CharacterJson())), references);
        var nodes = Flatten(document.BuildView()).ToArray();
        var graph = Assert.Single(nodes.OfType<GraphNode>());
        Assert.NotEmpty(graph.TextAlternative);
        Assert.All(graph.Items, item => Assert.NotNull(item.Target));
        var map = Assert.Single(nodes.OfType<MapNode>());
        Assert.NotEmpty(map.TextAlternative);
        Assert.NotNull(map.ExternalSource);
        Assert.All(map.Markers, marker => Assert.Null(marker.X));
    }

    private static IEnumerable<ViewNode> Flatten(ViewNode node)
    {
        yield return node;
        switch (node)
        {
            case TabsNode tabs:
                foreach (var child in tabs.Tabs.SelectMany(tab => tab.Children).SelectMany(Flatten)) yield return child;
                break;
            case SectionNode section:
                foreach (var child in section.Children.SelectMany(Flatten)) yield return child;
                break;
        }
    }

    private static string CharacterJson() => JsonSerializer.Serialize(new
    {
        SerializeMeta = new { HardFormat = "FCharacterSaveV1", SoftVersion = 2, FormatHash = 3777917472 },
        CharacterSaveV1 = new
        {
            MetaData = new { IsOnline = false, CurrentLocation = "SW.Area.Camp", Level = 1 },
            Ability = new { Attributes = new[] { new { AttributeName = "Emeralds", CurrentValue = 1 } } },
            Inventory = new { Entries = new object[]
            {
                new { ItemData = new { TypeTag = "SW.Item.Sword", RarityTag = "SW.Rarity.Common", TargetSlotOverride = "None" }, StackCount = 1 },
                new { ItemData = new { TypeTag = "SW.Item.Bow", RarityTag = "SW.Rarity.Rare", TargetSlotOverride = "Merchant" }, StackCount = 1 },
            } },
            quest = new { FocusedQuestId = "CA00", Quests = new[] { new { QuestName = "CA00", State = "Available", TaskData = Array.Empty<object>() } } },
            WorldExploration = new { LastMinecartStation = "SW.MinecartStation.Town.Fountain" },
        },
    });
}
