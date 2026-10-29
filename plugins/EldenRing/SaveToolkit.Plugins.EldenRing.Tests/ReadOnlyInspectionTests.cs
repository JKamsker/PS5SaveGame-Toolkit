using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class ReadOnlyInspectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SampleInspectAndBrowseOnlyPickerPreserveBytesRevisionAndHistory(bool readOnly)
    {
        var file = WriteTestSupport.Sample();
        var original = file.ReadAllBytes();
        if (readOnly) BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(SyntheticSave.UserData), 91);
        var input = WriteTestSupport.File(original);
        var plugin = new EldenRingPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, file.Directory.TitleId);
        using var document = plugin.Open(input, references);
        using var session = new DocumentSession("eldenring-inspect", file, plugin, document, references: references);
        var root = Assert.IsType<TabsNode>(session.BuildView());
        var action = Assert.Single(root.Tabs.Single(tab => tab.Title == "Reference catalog").Children.OfType<ActionNode>());
        Assert.False(action.Mutates);
        Assert.False(action.SupportsPlanning);
        Assert.Equal(!readOnly, session.CanEdit);
        _ = session.Expand("characters/0");
        var inventory = session.QueryCollection("slots/0/inventory/held", new() { Text = "Rowa Fruit" }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        var item = Assert.IsType<FieldNode>(Assert.Single(inventory.Rows).Cells["item"]);
        Assert.True(item.Spec.ReadOnly);
        foreach (var sort in Enum.GetValues<ReferenceSortKey>())
        {
            var query = new ReferenceQuery { Category = "goods", SortBy = sort, Descending = true, Offset = 1, Limit = 3 };
            var candidates = session.SearchReferences(item.ValueId, query);
            Assert.Contains(sort, candidates.SortKeys);
            var expected = references.CatalogForHost(ReferenceBrowser.CatalogId)!.Search(query);
            Assert.Equal(expected.Items.Select(record => record.Key), candidates.Items.Select(candidate => candidate.RawId));
            Assert.All(candidates.Items, candidate => Assert.Equal("goods", candidate.Category));
        }
        Assert.False(session.ApplyEdits([WriteTestSupport.Incoming(item.ValueId, "0x40002774")], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        if (!readOnly) Assert.True(session.ApplyEdits([WriteTestSupport.Incoming("slots/0/runes", 1146)], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var before = WriteTestSupport.Bytes(document);
        var state = session.State;
        var result = session.InvokeAction(action.ActionId, new Dictionary<string, JsonElement>
            { ["item"] = JsonSerializer.SerializeToElement("0x400050F0") }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Ok, string.Join("; ", result.Errors));
        Assert.Contains("Rowa Fruit", result.Message);
        Assert.Contains("MIT", result.Message);
        Assert.Empty(result.Changes);
        Assert.False(result.StructureChanged);
        Assert.Equal(state, session.State);
        var invalid = session.InvokeAction(action.ActionId, new Dictionary<string, JsonElement>
            { ["item"] = JsonSerializer.SerializeToElement("0x4000FFFF") }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(invalid.Ok);
        Assert.Equal(state, session.State);
        WriteTestSupport.EqualBytes(before, document);
        if (!readOnly)
        {
            Assert.True(session.Undo().Ok);
            WriteTestSupport.EqualBytes(original, document);
            Assert.True(session.Redo().Ok);
            WriteTestSupport.EqualBytes(before, document);
        }
        else Assert.False(session.State.CanUndo);
    }
}
