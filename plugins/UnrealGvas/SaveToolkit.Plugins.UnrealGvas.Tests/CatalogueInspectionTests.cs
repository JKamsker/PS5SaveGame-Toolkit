using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class CatalogueInspectionTests
{
    [Theory]
    [InlineData("PPSA01288", "General", "SaveGame")]
    [InlineData("PPSA02199", "ue4savegame.dpx.sav", "SaveGame00")]
    [InlineData("PPSA02199", "ue4savegame.dpx.sav", "GameUserSettings")]
    [InlineData("PPSA01603", "ue4savegame.ps5.sav", "HL0010")]
    [InlineData("PPSA01603", "ue4savegame.ps5.sav", "SaveGameList")]
    public void InspectionAndBrowseOnlyFieldsPreserveBytesRevisionAndHistory(string title, string name, string directory)
    {
        var sample = SampleSaves.Load(title).AllDiskFiles.First(file => file.FileName == name && file.Directory.Name == directory);
        var plugin = new UnrealGvasPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, title);
        using var document = plugin.Open(sample, references);
        using var session = new DocumentSession("inspect", sample, plugin, document, references: references);
        var nodes = Walk(session.BuildView()).ToArray();
        var actions = nodes.OfType<ActionNode>().Where(action => !action.Mutates).ToArray();
        Assert.NotEmpty(actions);
        var before = session.State;
        foreach (var action in actions)
        {
            var result = session.InvokeAction(action.ActionId, new Dictionary<string, JsonElement>(), before.Revision);
            Assert.True(result.Ok, string.Join("; ", result.Errors.Select(error => error.Message)));
            Assert.Contains("reference records", result.Message);
            Assert.Contains("Declared sorting: Key, Name, Category", result.Message);
            Assert.Empty(result.Changes);
            Assert.False(result.StructureChanged);
            Assert.Equal(before, result.State);
        }
        foreach (var field in nodes.OfType<FieldNode>().Where(field => field.Spec is ReferenceSpec))
        {
            Assert.True(field.Spec.ReadOnly);
            Assert.Equal(field.Value, document.GetValue(field.ValueId));
            var page = session.SearchReferences(field.ValueId, new() { Limit = 5, SortBy = ReferenceSortKey.Name });
            Assert.NotEmpty(page.Items);
            Assert.All(page.Items, candidate => Assert.NotNull(candidate.Dataset));
            var rejected = session.ApplyEdits([new IncomingEdit(field.ValueId, JsonSerializer.SerializeToElement(page.Items[0].RawId))]);
            Assert.False(rejected.Ok);
        }
        Assert.Equal(before, session.State);
        Assert.True(sample.ReadAllBytes().AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
        Assert.Empty(session.Serialize());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HogwartsBrowseOnlyCatalogueFiltersAndSortsAcrossPagesOnReadOnlyDocument(bool synthetic)
    {
        var plugin = new UnrealGvasPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01603");
        // A missing database pair keeps inspection available while all writes remain disabled.
        using var imported = synthetic ? HogwartsInventoryFixture.CreateDisk() : null;
        var slot = imported?.File ?? SampleSaves.Load("PPSA01603").AllDiskFiles.First(file => file.Directory.Name == "HL0010");
        var bytes = slot.ReadAllBytes();
        var gvas = GvasFile.Parse(bytes);
        bytes[gvas.Properties.Single(node => node.Name == "RawDatabaseImage").Offset + sizeof(int)] ^= 1;
        var overlay = new OverlaySaveSet(slot.Directory.SaveSet, new Dictionary<string, ReadOnlyMemory<byte>> { [slot.RelativePath] = bytes });
        var current = overlay.Directories.SelectMany(directory => directory.Files).Single(file => file.RelativePath == slot.RelativePath);
        using var document = plugin.Open(current, references);
        using var session = new DocumentSession("readonly", slot, plugin, document, references: references);
        Assert.False(session.CanEdit);
        var field = Walk(session.BuildView()).OfType<FieldNode>().Single(node =>
            node.Spec is ReferenceSpec { CatalogId: "unrealgvas_hogwarts_collectibles" });
        var catalogue = references.Catalog("unrealgvas_hogwarts_collectibles")!;
        string category = catalogue.Categories.OrderByDescending(category => catalogue.Search(new() { Category = category }).Total).First();
        foreach (var sort in Enum.GetValues<ReferenceSortKey>())
        foreach (bool descending in new[] { false, true })
            AssertCatalogueQuery(session, field.ValueId, catalogue, category,
                new ReferenceQuery { Category = category.ToUpperInvariant(), SortBy = sort, Descending = descending, Limit = 7 });
        var before = session.State;
        var action = Walk(session.BuildView()).OfType<ActionNode>().Single(node => node.ActionId.EndsWith(catalogue.Id, StringComparison.Ordinal));
        var result = session.InvokeAction(action.ActionId, new Dictionary<string, JsonElement>());
        Assert.True(result.Ok, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Equal(before, session.State);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void InspectionPreservesExistingEditsAndUndoHistory()
    {
        var sample = SampleSaves.Load("PPSA02199").AllDiskFiles.First(file =>
            file.Directory.Name == "GameUserSettings" && file.FileName == "ue4savegame.dpx.sav");
        var plugin = new UnrealGvasPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA02199");
        using var document = plugin.Open(sample, references);
        using var session = new DocumentSession("dirty-inspect", sample, plugin, document, references: references);
        var action = Walk(session.BuildView()).OfType<ActionNode>().First(node => !node.Mutates);
        var field = session.Expand("search").SelectMany(Walk).OfType<FieldNode>().First(node =>
            node.Spec is FloatSpec { ReadOnly: false } && node.Label.StartsWith("HDRBrightness", StringComparison.Ordinal));
        var edit = session.ApplyEdits([new IncomingEdit(field.ValueId, JsonSerializer.SerializeToElement(field.Value.AsDouble() + 0.1))]);
        Assert.True(edit.Ok);
        var before = session.State;
        Assert.True(before.CanUndo);
        Assert.True(before.IsDirty);
        var bytes = Assert.Single(document.Serialize()).Content.ToArray();
        var result = session.InvokeAction(action.ActionId, new Dictionary<string, JsonElement>(), before.Revision);
        Assert.True(result.Ok, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Equal(before, result.State);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Undo().Ok);
        Assert.Equal(field.Value, document.GetValue(field.ValueId));
        Assert.True(sample.ReadAllBytes().AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
    }

    [Fact]
    public void MissingReferencePacksPublishNoCatalogueActionsOrExampleFields()
    {
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("generic.sav", GvasFixture.Primitives()));
        var nodes = Walk(document.BuildView()).ToArray();
        Assert.DoesNotContain(nodes.OfType<ActionNode>(), node => !node.Mutates);
        Assert.DoesNotContain(nodes.OfType<FieldNode>(), node => node.Spec is ReferenceSpec);
    }

    private static void AssertCatalogueQuery(DocumentSession session, string valueId, IReferenceCatalog catalogue,
        string category, ReferenceQuery query)
    {
        var expected = new List<ReferenceRecord>();
        for (int offset = 0; ; offset += ReferenceQuery.MaxLimit)
        {
            var page = catalogue.Search(query with { Offset = offset, Limit = ReferenceQuery.MaxLimit });
            expected.AddRange(page.Items);
            if (expected.Count >= page.Total) break;
        }
        IOrderedEnumerable<ReferenceRecord> ordered = query.SortBy switch
        {
            ReferenceSortKey.Key => query.Descending ? expected.OrderByDescending(record => record.Key, StringComparer.OrdinalIgnoreCase) : expected.OrderBy(record => record.Key, StringComparer.OrdinalIgnoreCase),
            ReferenceSortKey.Name => query.Descending ? expected.OrderByDescending(record => record.Name, StringComparer.OrdinalIgnoreCase) : expected.OrderBy(record => record.Name, StringComparer.OrdinalIgnoreCase),
            _ => query.Descending ? expected.OrderByDescending(record => record.Category, StringComparer.OrdinalIgnoreCase) : expected.OrderBy(record => record.Category, StringComparer.OrdinalIgnoreCase),
        };
        Assert.Equal(ordered.ThenBy(record => record.Key, StringComparer.Ordinal).Select(record => record.Key), expected.Select(record => record.Key));
        var found = new List<string>();
        for (int offset = 0; offset < expected.Count; offset += query.Limit)
        {
            var page = session.SearchReferences(valueId, query with { Offset = offset });
            Assert.Equal(expected.Count, page.Total);
            Assert.Equal(Enum.GetValues<ReferenceSortKey>(), page.SortKeys);
            Assert.All(page.Items, candidate => Assert.Equal(category, candidate.Category));
            found.AddRange(page.Items.Select(candidate => candidate.RawId));
        }
        Assert.Equal(expected.Select(record => record.Key), found);
        var searched = session.SearchReferences(valueId, query with { Text = expected[0].Key });
        Assert.Contains(searched.Items, candidate => candidate.RawId == expected[0].Key);
    }

    private static IEnumerable<ViewNode> Walk(ViewNode node)
    {
        yield return node;
        var children = node switch
        {
            TabsNode tabs => tabs.Tabs.SelectMany(tab => tab.Children),
            SectionNode section => section.Children,
            TableNode table => table.Rows.SelectMany(row => row.Cells.Values),
            _ => [],
        };
        foreach (var child in children)
        foreach (var descendant in Walk(child)) yield return descendant;
    }
}
