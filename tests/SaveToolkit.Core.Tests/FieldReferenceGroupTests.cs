using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class FieldReferenceGroupTests
{
    [Fact]
    public void MemberMappingQualifiesCanonicalTargetsInEagerLazyAndQueryViews()
    {
        using var memberDocument = new FieldReferenceDocument();
        var member = new DocumentMemberAdapter("profile", memberDocument);
        using var group = new MappedDocument(member);
        var index = new ViewIndex();
        var view = group.BuildView();
        index.Add(view);
        var resolved = Assert.IsType<SectionNode>(index.ResolveFieldReferences(group, view));
        var field = Assert.IsType<FieldNode>(resolved.Children[0]);
        Assert.Equal("profile/quantity", field.ValueId);
        Assert.Equal(member.Prefix("missing"), Assert.IsType<FieldReferenceNode>(resolved.Children[3]).TargetValueId);
        Assert.False(index.KnowsBinding("quantity"));
        var lazy = group.ExpandNode("profile/more");
        index.AddAll(lazy);
        var tabs = Assert.IsType<TabsNode>(index.ResolveFieldReferences(group, Assert.Single(lazy)));
        var table = Assert.IsType<TableNode>(Assert.Single(Assert.Single(tabs.Tabs).Children));
        Assert.Equal(field.ValueId, Assert.IsType<FieldNode>(Assert.Single(table.Rows).Cells["value"]).ValueId);
        var page = member.Query("profile/items", new() { Limit = 1 });
        var reference = Assert.IsType<FieldReferenceNode>(Assert.Single(page.Rows).Cells["value"]);
        Assert.Equal(field.ValueId, reference.TargetValueId);
        var projected = Assert.IsType<FieldNode>(index.ResolveFieldReferences(group, reference));
        Assert.Equal(2, projected.Value.AsInt32());
        Assert.Single(group.ApplyEdits([new(field.ValueId, FieldValue.Of(7))]).Changes);
        Assert.Equal(7, memberDocument.GetValue("quantity").AsInt32());
    }

    private sealed class MappedDocument(DocumentMemberAdapter member) : ISaveDocument
    {
        public string Title => member.Document.Title;
        public bool CanEdit => member.Document.CanEdit;
        public ViewNode BuildView() => member.Node(member.Document.BuildView());
        public IReadOnlyList<ViewNode> ExpandNode(string lazyId) => member.ExpandNode(lazyId);
        public FieldValue GetValue(string valueId) => member.GetValue(valueId);
        public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) => member.ApplyEdits(edits);
        public ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) => member.InvokeAction(actionId, parameters);
        public DocumentMemento RestoreMemento(DocumentMemento memento) => member.Document.RestoreMemento(memento);
        public IReadOnlyList<FileOutput> Serialize() => member.Document.Serialize();
        public AssetContent? GetAsset(string assetId) => null;
        public ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count) => member.ReadBlob(blobId, offset, count);
        public void Dispose() { } // Non-owning: the fixture disposes the original member.
    }
}
