using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseInspectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InspectionWorksThroughCoreWithoutChangingBytesRevisionOrHistory(bool readOnly)
    {
        var plugin = new TalesOfArisePlugin();
        var file = Assert.IsType<DiskSaveFile>(AriseSampleTests.GameplayFiles().First());
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA02745");
        using var original = plugin.Open(file, references);
        using var document = readOnly ? new ReadOnlyDocument(original) : original;
        using var session = new DocumentSession("arise-inspect", file, plugin, document, references: references);
        var view = session.BuildView();
        Assert.Equal(!readOnly, session.CanEdit);
        Assert.All(Actions(view).Where(action => action.ActionId.StartsWith("references/inspect/", StringComparison.Ordinal)),
            action => Assert.False(action.Mutates));

        if (!readOnly)
        {
            Assert.True(session.ApplyEdits([new("party/gald", JsonSerializer.SerializeToElement("123"))]).Ok);
        }

        InspectAndCheck();
        if (!readOnly)
        {
            Assert.True(session.Undo().Ok);
            Assert.True(session.State.CanRedo);
            InspectAndCheck();
        }

        void InspectAndCheck()
        {
            var before = session.State;
            var bytes = Assert.Single(document.Serialize()).Content.ToArray();
            foreach (var catalogId in new[] { "items", "artes", "skills" })
            {
                var record = references.Catalog(catalogId)!.Search(new() { Limit = 1 }).Items[0];
                var result = session.InvokeAction($"references/inspect/{catalogId}", new Dictionary<string, JsonElement>
                {
                    ["record"] = JsonSerializer.SerializeToElement(record.Key),
                }, before.Revision);
                Assert.True(result.Ok, string.Join(';', result.Errors.Select(error => error.Message)));
                Assert.Contains(record.Name, result.Message);
                Assert.Contains("CC-BY-SA-3.0", result.Message);
                Assert.Empty(result.Changes);
                Assert.False(result.StructureChanged);
                Assert.Equal(before, result.State);
            }

            var invalid = session.InvokeAction("references/inspect/items", new Dictionary<string, JsonElement>
            {
                ["record"] = JsonSerializer.SerializeToElement("missing-reference"),
            });
            Assert.False(invalid.Ok);
            Assert.Equal(before, session.State);
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void ReadOnlyIdentityPickersBrowseCategoriesAndSortWithoutEnablingEdits()
    {
        var plugin = new TalesOfArisePlugin();
        var file = Assert.IsType<DiskSaveFile>(AriseSampleTests.GameplayFiles().First());
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA02745");
        using var document = plugin.Open(file, references);
        using var session = new DocumentSession("arise-browse", file, plugin, document, references: references);
        _ = session.BuildView();
        foreach (var catalogId in new[] { "items", "artes", "skills" })
        {
            var row = Assert.Single(session.QueryCollection(catalogId, new() { Limit = 1 }).Rows);
            var identity = Assert.IsType<FieldNode>(row.Cells["id"]);
            var spec = Assert.IsType<ReferenceSpec>(identity.Spec);
            Assert.True(spec.ReadOnly);
            Assert.True(spec.AllowCustom);
            var inspect = Assert.IsType<ActionNode>(row.Cells["inspect"]);
            Assert.False(inspect.Mutates);
            Assert.True(Assert.IsType<ReferenceSpec>(Assert.Single(inspect.Parameters).Spec).ReadOnly);
            var catalog = references.Catalog(catalogId)!;
            var category = catalog.Categories[0];
            var page = session.SearchReferences(identity.ValueId, new()
            {
                Category = category, SortBy = ReferenceSortKey.Name, Descending = true, Limit = 3,
            });
            Assert.Contains(ReferenceSortKey.Category, page.SortKeys);
            Assert.All(page.Items, item => Assert.Equal(category, item.Category));
            Assert.Equal(catalog.Search(new() { Category = category, SortBy = ReferenceSortKey.Name, Descending = true, Limit = 3 })
                .Items.Select(record => record.Key), page.Items.Select(item => item.RawId));
            Assert.False(session.ApplyEdits([new(identity.ValueId, JsonSerializer.SerializeToElement(page.Items[0].RawId))]).Ok);
        }

        _ = session.Expand("inventory");
        var inventory = session.QueryCollection("inventory", new());
        var unknown = inventory.Rows.Select(row => Assert.IsType<FieldNode>(row.Cells["id"]))
            .First(field => references.Catalog("items")!.Find(field.Value.AsString()) is null);
        Assert.True(Assert.IsType<ReferenceSpec>(unknown.Spec).ReadOnly);
        Assert.True(Assert.IsType<ReferenceSpec>(unknown.Spec).AllowCustom);
        Assert.NotEmpty(session.SearchReferences(unknown.ValueId, new() { Limit = 1 }).Items);
        Assert.False(session.State.IsDirty);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("items")]
    [InlineData("artes")]
    [InlineData("skills")]
    [InlineData("inventory")]
    public void FixedRowInspectionWorksThroughCoreWithDefaultsAndRejectsSubstitution(string collectionId)
    {
        var plugin = new TalesOfArisePlugin();
        var file = Assert.IsType<DiskSaveFile>(AriseSampleTests.GameplayFiles().First(file => file.FileName == "SaveData_002.sav"));
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA02745");
        using var document = plugin.Open(file, references);
        using var session = new DocumentSession("arise-fixed-inspect", file, plugin, document, references: references);
        _ = session.BuildView();
        if (collectionId == "inventory") _ = session.Expand("inventory");
        var rows = session.QueryCollection(collectionId, new()).Rows;
        var expectedActions = collectionId == "inventory" ? 1 : 2;
        var actions = rows.Select(row => row.Cells["inspect"]).OfType<ActionNode>().Take(expectedActions).ToArray();
        Assert.Equal(expectedActions, actions.Length);
        var catalog = references.Catalog(collectionId == "inventory" ? "items" : collectionId)!;
        var before = session.State;
        var bytes = Assert.Single(document.Serialize()).Content.ToArray();
        foreach (var action in actions)
        {
            var result = session.InvokeAction(action.ActionId, new Dictionary<string, JsonElement>(), before.Revision);
            Assert.True(result.Ok, string.Join(';', result.Errors.Select(error => error.Message)));
            var record = catalog.Find(Assert.Single(action.Parameters).Value.AsString())!;
            Assert.Contains(record.Name, result.Message);
            Assert.Equal(before, result.State);
        }
        var other = catalog.Search(new() { Limit = 2 }).Items
            .First(record => record.Key != Assert.Single(actions[0].Parameters).Value.AsString());
        var rejected = session.InvokeAction(actions[0].ActionId, new Dictionary<string, JsonElement>
        {
            ["record"] = JsonSerializer.SerializeToElement(other.Key),
        });
        Assert.False(rejected.Ok);
        Assert.Equal(before, session.State);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static IEnumerable<ActionNode> Actions(ViewNode node) => node switch
    {
        ActionNode action => [action],
        TabsNode tabs => tabs.Tabs.SelectMany(tab => tab.Children).SelectMany(Actions),
        SectionNode section => section.Children.SelectMany(Actions),
        _ => [],
    };

    private sealed class ReadOnlyDocument(ISaveDocument inner) : ISaveDocument
    {
        public string Title => inner.Title;
        public bool CanEdit => false;
        public ViewNode BuildView() => inner.BuildView();
        public IReadOnlyList<ViewNode> ExpandNode(string lazyId) => inner.ExpandNode(lazyId);
        public FieldValue GetValue(string valueId) => inner.GetValue(valueId);
        public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) => EditOutcome.Rejected("", "Read-only test document.");
        public ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) => inner.InvokeAction(actionId, parameters);
        public IReadOnlyList<FileOutput> Serialize() => inner.Serialize();
        public void Dispose() { }
    }
}
