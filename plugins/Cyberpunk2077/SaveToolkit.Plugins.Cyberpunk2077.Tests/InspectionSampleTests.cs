using System.Diagnostics;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class InspectionSampleTests(ITestOutputHelper output)
{
    [Fact]
    public void SampleQueriesResolveAllItemsAnd1129FactsWithAttributionAndReadOnlyBindings()
    {
        var file = ProgressionSampleTests.Files()[0];
        var (plugin, _, context) = ReferencePackFixture.Embedded();
        var timer = Stopwatch.StartNew();
        using var document = plugin.Open(file, context);
        using var session = new DocumentSession("inspection", (DiskSaveFile)file, plugin, document, references: context);
        var view = Assert.IsType<TabsNode>(session.BuildView());
        output.WriteLine($"Open + initial view with pinned packs: {timer.Elapsed.TotalMilliseconds:F1} ms");
        Assert.All(view.Tabs.Single(tab => tab.Title == "Facts").Children, node => Assert.IsType<QueryableTableNode>(node));
        var inventory = session.QueryCollection("inventory", new QueryableCollectionQuery { Limit = 200 });
        Assert.Equal(22, inventory.TotalCount);
        Assert.Equal(22, inventory.Rows.Count);
        Assert.All(inventory.Rows, row => Assert.Contains("MIT", Assert.IsType<FieldNode>(row.Cells["id"]).Description));
        var facts = new List<TableRow>();
        for (var offset = 0; offset < 2750; offset += 200)
        {
            var page = session.QueryCollection("facts", new QueryableCollectionQuery { Offset = offset, Limit = 200 });
            Assert.Equal(2750, page.TotalCount);
            facts.AddRange(page.Rows);
        }

        Assert.Equal(2750, facts.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(1129, facts.Count(row => Assert.IsType<FieldNode>(row.Cells["name"]).Confidence == Confidence.Inferred));
        foreach (var row in facts)
        {
            var field = Assert.IsType<FieldNode>(row.Cells["value"]);
            Assert.False(field.Spec.ReadOnly);
            Assert.Equal(field.Value, document.GetValue(field.ValueId));
        }

        var named = facts.First(row => Assert.IsType<FieldNode>(row.Cells["name"]).Confidence == Confidence.Inferred);
        var name = Assert.IsType<FieldNode>(named.Cells["name"]);
        Assert.Contains("MIT", name.Description);
        var found = session.QueryCollection("facts", new QueryableCollectionQuery { Text = name.Value.AsString() });
        Assert.Contains(found.Rows, row => row.Key == named.Key);
        Assert.False(session.ApplyEdits([new(Assert.IsType<FieldNode>(named.Cells["value"]).ValueId, JsonSerializer.SerializeToElement("-1"))]).Ok);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        Assert.All(context.Datasets(), dataset => Assert.True(dataset.Used));
    }

    [Fact]
    public void QuantityQueriesFollowEditsAndUndoWithoutChangingInstanceKeys()
    {
        var file = ProgressionSampleTests.Files()[0];
        var (plugin, _, context) = ReferencePackFixture.Embedded();
        using var document = plugin.Open(file, context);
        using var session = new DocumentSession("inventory", (DiskSaveFile)file, plugin, document, references: context);
        _ = session.BuildView();
        var before = session.QueryCollection("inventory", new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["id"] = "Ammo.HandgunAmmo" } });
        var row = Assert.Single(before.Rows);
        var field = Assert.IsType<FieldNode>(row.Cells["quantity"]);
        Assert.True(session.ApplyEdits([new(field.ValueId, JsonSerializer.SerializeToElement("789"))]).Ok);
        var edited = session.QueryCollection("inventory", new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["quantity"] = "789" } });
        Assert.Equal(row.Key, Assert.Single(edited.Rows).Key);
        Assert.True(session.Undo().Ok);
        Assert.Empty(session.QueryCollection("inventory", new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["quantity"] = "789" } }).Rows);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void ReadOnlyBudgetQueriesFollowPointEditsAndUndoIncludingOmittedProperties()
    {
        var file = ProgressionSampleTests.Files()[0];
        var (plugin, _, context) = ReferencePackFixture.Embedded();
        using var document = plugin.Open(file, context);
        using var session = new DocumentSession("budgets", (DiskSaveFile)file, plugin, document, references: context);
        _ = session.BuildView();
        var query = new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["kind"] = "Point budget", ["group"] = "Primary" } };
        var before = Assert.Single(session.QueryCollection("development", query).Rows);
        Assert.Equal("0", ReadOnlyInspectionSampleTests.Cell(before, "unspent"));
        Assert.True(session.ApplyEdits([new("progression/points/Primary", JsonSerializer.SerializeToElement("9"))]).Ok);
        var after = Assert.Single(session.QueryCollection("development", query).Rows);
        Assert.Equal(before.Key, after.Key);
        Assert.Equal("9", ReadOnlyInspectionSampleTests.Cell(after, "unspent"));
        Assert.True(session.Undo().Ok);
        Assert.Equal("0", ReadOnlyInspectionSampleTests.Cell(Assert.Single(session.QueryCollection("development", query).Rows), "unspent"));
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void HostPickerValidatesCatalogChoicesAndInspectionPreservesUndoHistory()
    {
        var file = ProgressionSampleTests.Files()[0];
        var (plugin, _, context) = ReferencePackFixture.Embedded();
        using var document = plugin.Open(file, context);
        using var session = new DocumentSession("picker", (DiskSaveFile)file, plugin, document, references: context);
        _ = session.BuildView();
        const string actionId = "inspect-catalog/items";
        var candidates = session.SearchReferences("record", new ReferenceQuery { Text = "HandgunAmmo" }, actionId: actionId);
        Assert.NotEmpty(candidates.Items);
        Assert.All(candidates.Items, candidate => Assert.Equal("MIT", candidate.Dataset!.Attribution.License));
        Assert.True(session.ApplyEdits([new("inventory/39/quantity", JsonSerializer.SerializeToElement("777"))]).Ok);
        var stateBefore = session.State;
        var bytesBefore = Assert.Single(document.Serialize()).Content.ToArray();
        var inspection = session.InvokeAction(actionId, new Dictionary<string, JsonElement>
            { ["record"] = JsonSerializer.SerializeToElement(CyberpunkReferences.ItemKey(InventoryReader.MoneyTweakId)) });
        Assert.True(inspection.Ok);
        Assert.Contains("Items.money", inspection.Message);
        Assert.Empty(inspection.Changes);
        Assert.Equal(stateBefore, session.State);
        Assert.Equal(bytesBefore, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(inspection.State.IsDirty);
        Assert.True(inspection.State.CanUndo);
        Assert.False(session.InvokeAction(actionId, new Dictionary<string, JsonElement> { ["record"] = JsonSerializer.SerializeToElement("unknown") }).Ok);
        Assert.True(session.Undo().Ok);
        var redoState = session.State;
        Assert.True(redoState.CanRedo);
        Assert.True(session.InvokeAction(actionId, new Dictionary<string, JsonElement>
            { ["record"] = JsonSerializer.SerializeToElement(CyberpunkReferences.ItemKey(InventoryReader.MoneyTweakId)) }).Ok);
        Assert.Equal(redoState, session.State);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("2210", "Synthetic localized money")]
    [InlineData("2500", "Items.money")]
    public void UserLocalizationIsPinnedByNativeBuildAndCannotAuthorizeWrites(string build, string expectedName)
    {
        var (plugin, store, _) = ReferencePackFixture.Embedded();
        store.Import(ReferencePackFixture.UserPack("items", [new { id = CyberpunkReferences.ItemKey(InventoryReader.MoneyTweakId),
            name = "Synthetic localized money", category = "Ammo", limit = 1 }], build), "localization.zip");
        var file = ProgressionSampleTests.Files()[0];
        var context = store.Pin(plugin, "PPSA04027");
        using var document = plugin.Open(file, context);
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var rows = query.Query("inventory", new QueryableCollectionQuery { Limit = 200 }).Rows;
        Assert.Equal(expectedName, Assert.IsType<FieldNode>(Assert.Single(rows, row => row.Key == "39").Cells["id"]).Value.AsString());
        Assert.Equal(100_000_000, Assert.IsType<IntegerSpec>(Assert.IsType<FieldNode>(Assert.Single(rows, row => row.Key == "39").Cells["quantity"]).Spec).Max);
        Assert.All(rows.Where(row => row.Key is not ("39" or "44" or "45" or "49" or "50" or "51")), row =>
        {
            if (row.Cells["quantity"] is FieldNode field)
            {
                Assert.True(field.Spec.ReadOnly);
            }
        });
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void MissingPacksRetainBaselineNamesAndRawFactHashes()
    {
        var file = ProgressionSampleTests.Files()[0];
        using var document = new Cyberpunk2077Plugin().Open(file, IReferenceContext.Empty);
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var rows = query.Query("inventory", new QueryableCollectionQuery { Limit = 200 }).Rows;
        Assert.All(rows, row => Assert.Contains("MIT", Assert.IsType<FieldNode>(row.Cells["id"]).Description));
        Assert.All(query.Query("facts", new QueryableCollectionQuery()).Rows, row =>
            Assert.Equal(Confidence.Unknown, Assert.IsType<FieldNode>(row.Cells["name"]).Confidence));
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void LargeInventoryUsesQueryableShellAndConstructsOnlyRequestedRows()
    {
        var container = CsavContainer.Parse(ProgressionSampleTests.Files()[0].ReadAllBytes());
        const int count = 1000;
        var nodes = Enumerable.Range(0, count).Select(id => new CsavNode(id, "itemData", -1, -1, 0, 29)).ToArray();
        var items = Enumerable.Range(0, count).Select(id => new InventoryItem(id, 1, InventoryReader.MoneyTweakId, null, 0)).ToArray();
        var bound = 0;
        var inspection = new InventoryInspection(items, container, new CsavTree(nodes, []), new CyberpunkReferences(IReferenceContext.Empty, 2210),
            new ItemNames(), container.Payload, _ => throw new InvalidOperationException("No quantity in synthetic rows"),
            (id, label, spec, value) => { bound++; return new FieldNode(id, label, spec with { ReadOnly = true }, FieldValue.Of(value)); });
        Assert.Single(inspection.Build().OfType<QueryableTableNode>());
        Assert.Equal(0, bound);
        var page = inspection.Query(new QueryableCollectionQuery { Offset = 700, Limit = 3 });
        Assert.Equal(3, page.Rows.Count);
        Assert.Equal(6, bound); // One display-name and one reference field per returned row.
        Assert.Equal(1000, page.TotalCount);
        Assert.Equal(3, page.Rows.Select(row => row.Key).Distinct().Count());
    }
}
