using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherInspectionTests
{
    [Fact]
    public void AllCatalogLookupsWorkOnReadOnlyDocumentsWithoutChangingStateOrBytes()
    {
        var temporary = Directory.CreateTempSubdirectory("witcher3-inspection-");
        try
        {
            var input = SyntheticSave.Create();
            var file = WitcherTestFiles.Import(input, temporary.FullName);
            var plugin = new Witcher3Plugin();
            var references = ReferencePackFixture.Pin(plugin);
            using var document = plugin.Open(file, references);
            using var session = new DocumentSession("inspection", file, plugin, document, references: references);
            Assert.False(session.CanEdit);
            var actions = Nodes(session.BuildView()).OfType<ActionNode>().ToArray();
            var before = session.State;
            foreach (var (id, parameter, key, label) in new[] {
                (InventoryViews.LookupActionId, "item", "Bread", "Bread"),
                ("reference/inspect/items", "record", "Bread", "Bread"),
                ("reference/inspect/abilities", "record", "S_Sword_s01", "Whirl"),
                ("reference/inspect/gwent", "record", "108", "Yarpen Zigrin"),
            })
            {
                var action = Assert.Single(actions, action => action.ActionId == id);
                Assert.False(action.Mutates);
                Assert.False(action.SupportsPlanning);
                var result = session.InvokeAction(id, new Dictionary<string, JsonElement> { [parameter] = JsonSerializer.SerializeToElement(key) }, before.Revision, cancellationToken: TestContext.Current.CancellationToken);
                Assert.True(result.Ok, string.Join("; ", result.Errors.Select(error => error.Message)));
                Assert.Contains(label, result.Message!, StringComparison.Ordinal);
                Assert.Contains(id.EndsWith("gwent", StringComparison.Ordinal) ? "MIT" : "CC-BY-SA-3.0", result.Message!, StringComparison.Ordinal);
                Assert.Empty(result.Changes);
                Assert.Equal(before, result.State);
                Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());
            }
            Assert.All(actions.Where(action => action.SupportsPlanning), action => Assert.True(action.Mutates));
            Assert.False(session.InvokeAction("reference/inspect/items", new Dictionary<string, JsonElement> {
                ["record"] = JsonSerializer.SerializeToElement("unknown-item"),
            }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
            Assert.Equal(before, session.State);
            Assert.Empty(session.Serialize(cancellationToken: TestContext.Current.CancellationToken));
        }
        finally { temporary.Delete(recursive: true); }
    }

    [Fact]
    public void SavedAbilityReferencesBrowseAttributedCategoriesAndSortWithoutAllowingIdentityEdits()
    {
        var plugin = new Witcher3Plugin();
        var references = ReferencePackFixture.Pin(plugin);
        var file = EnabledWriteFixture.Files()[0];
        using var document = plugin.Open(file, references);
        using var session = new DocumentSession("browse", file, plugin, document, references: references);
        _ = session.BuildView();
        foreach (var array in new[] { "skills", "mutations" })
        {
            var rows = session.QueryCollection(AbilityCollections.Prefix + array, new QueryableCollectionQuery { Limit = 200 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows;
            var field = Assert.IsType<FieldNode>(rows[0].Cells["id"]);
            var spec = Assert.IsType<ReferenceSpec>(field.Spec);
            Assert.Equal("abilities", spec.CatalogId);
            Assert.True(spec.ReadOnly);
            Assert.True(spec.AllowCustom);
            var before = session.State;
            var first = session.SearchReferences(field.ValueId, new ReferenceQuery { Limit = 1 });
            var category = Assert.Single(first.Items).Category;
            var query = new ReferenceQuery { Category = category, SortBy = ReferenceSortKey.Name, Descending = true, Limit = 3 };
            var page = session.SearchReferences(field.ValueId, query);
            Assert.Contains(ReferenceSortKey.Name, page.SortKeys);
            Assert.All(page.Items, candidate => {
                Assert.Equal(category, candidate.Category);
                Assert.Contains("CC-BY-SA-3.0", candidate.Dataset!.Attribution.ToShortText(), StringComparison.Ordinal);
            });
            Assert.Equal(references.Catalog("abilities")!.Search(query).Items.Select(record => record.Key), page.Items.Select(candidate => candidate.RawId));
            Assert.Null(session.FindReference(field.ValueId, "unmapped-native-ability").Label);
            Assert.False(session.ApplyEdits([EnabledWriteFixture.Incoming(field.ValueId, FieldValue.Of(page.Items[0].RawId))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
            Assert.Equal(before, session.State);
        }
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static IEnumerable<ViewNode> Nodes(ViewNode node)
    {
        yield return node;
        var children = node switch {
            TabsNode tabs => tabs.Tabs.SelectMany(tab => tab.Children),
            SectionNode section => section.Children,
            _ => [],
        };
        foreach (var child in children.SelectMany(Nodes)) { yield return child; }
    }
}
