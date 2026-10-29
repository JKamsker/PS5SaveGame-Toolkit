using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class FieldReferenceSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PriorReferencePageOrExpansionDoesNotBoundLaterOrdinaryRootOrExpansion(bool queryFirst)
    {
        using var fixture = new FieldReferenceSessionFixture(loaded: false);
        fixture.Document.ReferencesOnlyInPages = true;
        _ = fixture.Session.BuildView();
        if (queryFirst) _ = fixture.Session.QueryCollection("items", new() { Limit = 1 });
        else _ = fixture.Session.Expand("more");
        fixture.Document.LargeLegacyView = true;
        Assert.Equal(100_001, Assert.IsType<SectionNode>(fixture.Session.BuildView()).Children.Count);
        Assert.Equal(100_001, fixture.Session.Expand("more").Count);
        Assert.Empty(fixture.Session.Serialize());
        Assert.False(fixture.Session.State.IsDirty);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProjectionsShareCurrentValueSpecValidationAndHistoryWithoutOpeningCanonicalView(bool loaded)
    {
        using var fixture = new FieldReferenceSessionFixture(loaded);
        var before = fixture.Document.Serialize().Single().Content.ToArray();
        var view = Assert.IsType<SectionNode>(fixture.Session.CaptureView().View);
        var first = Assert.IsType<FieldNode>(view.Children[0]);
        var second = Assert.IsType<FieldNode>(view.Children[1]);
        Assert.Same(first.Spec, second.Spec);
        Assert.Equal(first.Value, second.Value);
        Assert.Equal("quantity", first.ValueId);
        Assert.Equal("Named projection", first.Label);
        Assert.Equal(Confidence.Inferred, first.Confidence);
        Assert.Equal("Unverified gameplay meaning.", first.Warning);
        Assert.Contains("Canonical explanation\nContext", first.Description);
        if (loaded) Assert.Same(first.Spec, Assert.IsType<FieldNode>(view.Children[^1]).Spec);
        Assert.IsType<FieldReferenceNode>(view.Children[3]);
        Assert.Empty(fixture.Session.Serialize());
        Assert.Equal(before, fixture.Document.Serialize().Single().Content.ToArray());
        Assert.Equal(0, fixture.Document.Expansions);
        Assert.Equal(0, fixture.Document.Queries);
        Assert.Equal(0, fixture.Document.Writes);
        Assert.False(fixture.Session.ApplyEdits([Edit("quantity", 11)]).Ok);
        Assert.False(fixture.Session.ApplyEdits([Edit("quantity", 6)]).Ok);
        Assert.False(fixture.Session.ApplyEdits([Edit("total", 5)]).Ok);
        Assert.False(fixture.Session.ApplyEdits([Edit("missing", 5)]).Ok);
        Assert.True(fixture.Session.ApplyEdits([Edit("quantity", 7)]).Ok);
        var changed = Assert.IsType<SectionNode>(fixture.Session.BuildView());
        Assert.Equal(7, Assert.IsType<FieldNode>(changed.Children[0]).Value.AsInt32());
        Assert.Equal(7, Assert.IsType<FieldNode>(changed.Children[1]).Value.AsInt32());
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(before, fixture.Document.Serialize().Single().Content.ToArray());
        Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(7, fixture.Document.GetValue("quantity").AsInt32());
    }

    [Fact]
    public void LazyNestedTablesAndQueryPagesResolveWithoutChangingSaveBytes()
    {
        using var fixture = new FieldReferenceSessionFixture(loaded: false);
        _ = fixture.Session.BuildView();
        var lazy = Assert.IsType<TabsNode>(Assert.Single(fixture.Session.Expand("more")));
        var table = Assert.IsType<TableNode>(Assert.Single(Assert.Single(lazy.Tabs).Children));
        var field = Assert.IsType<FieldNode>(Assert.Single(table.Rows).Cells["value"]);
        Assert.Equal("quantity", field.ValueId);
        var page = fixture.Session.QueryCollection("items", new() { Limit = 1 });
        Assert.Equal(field, Assert.IsType<FieldNode>(Assert.Single(page.Rows).Cells["value"]));
        Assert.Equal(1, fixture.Document.Expansions);
        Assert.Equal(1, fixture.Document.Queries);
        Assert.Empty(fixture.Session.Serialize());
        Assert.False(fixture.Session.State.IsDirty);
    }

    [Fact]
    public void StructuralRetirementCannotResurrectTargetsAndUndoRedoRebindThem()
    {
        using var fixture = new FieldReferenceSessionFixture(loaded: false);
        _ = fixture.Session.BuildView();
        _ = fixture.Session.Expand("more");
        var revision = fixture.Session.State.Revision;
        Assert.True(fixture.Session.InvokeAction("delete", new Dictionary<string, JsonElement>(), revision).Ok);
        Assert.IsType<FieldReferenceNode>(Assert.IsType<SectionNode>(fixture.Session.BuildView()).Children[0]);
        Assert.True(fixture.Session.ApplyEdits([Edit("quantity", 5)], revision).Stale);
        Assert.True(fixture.Session.ApplyEdits([Edit("quantity", 5)]).Stale);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.IsType<FieldNode>(Assert.IsType<SectionNode>(fixture.Session.BuildView()).Children[0]);
        Assert.True(fixture.Session.Redo().Ok);
        Assert.IsType<FieldReferenceNode>(Assert.IsType<SectionNode>(fixture.Session.BuildView()).Children[0]);
    }

    [Fact]
    public void ReadOnlyDocumentNeverGainsWritePermissionFromProjection()
    {
        using var fixture = new FieldReferenceSessionFixture(editable: false);
        var snapshot = fixture.Session.CaptureView();
        Assert.False(snapshot.CanEdit);
        Assert.IsType<FieldNode>(Assert.IsType<SectionNode>(snapshot.View).Children[0]);
        Assert.False(fixture.Session.ApplyEdits([Edit("quantity", 5)]).Ok);
        Assert.Equal(0, fixture.Document.Writes);
    }

    private static IncomingEdit Edit(string id, int value) => new(id, JsonSerializer.SerializeToElement(value));
}
