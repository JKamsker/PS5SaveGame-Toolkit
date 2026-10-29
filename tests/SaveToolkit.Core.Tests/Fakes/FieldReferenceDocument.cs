using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

internal sealed class FieldReferenceDocument : BoundSaveDocument, IFieldMetadataDocument, IQueryableDocument
{
    private int _quantity = 2;
    private bool _exists = true;
    public bool ShowCanonical { get; init; } = true;
    public bool Editable { get; init; } = true;
    public bool PublishPresentation { get; set; } = true;
    public bool LargeLegacyView { get; set; }
    public bool ReferencesOnlyInPages { get; set; }
    public FieldPresentation? AnnotationOverride { get; set; }
    public int Expansions { get; private set; }
    public int Queries { get; private set; }
    public int Writes { get; private set; }
    public List<string[]> MetadataRequests { get; } = [];
    public override string Title => "Field references";
    public override bool CanEdit => Editable;
    public IReadOnlyList<QueryableCollection> Collections => [new("items", "Items", [new("value", "Value")], 1)];

    private void Bind()
    {
        if (!_exists) return;
        RegisterField(new FieldMetadata("quantity", "Canonical quantity", new IntegerSpec(0, 10))
            { Presentation = PublishPresentation ? AnnotationOverride ?? new() { Confidence = Confidence.Inferred,
                Warning = "Unverified gameplay meaning.", Description = "Canonical explanation" } : null },
            () => FieldValue.Of(_quantity), value => { _quantity = value.AsInt32(); Writes++; },
            value => value.AsInt32() == 6 ? "Six is rejected." : null);
        RegisterField(new FieldMetadata("total", "Derived total", IntegerSpec.Int32 with { ReadOnly = true })
            { BindingKind = FieldBindingKind.Derived, Presentation = new() }, () => FieldValue.Of(_quantity * 2));
    }

    public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
    {
        MetadataRequests.Add(valueIds.ToArray());
        Bind();
        return _exists ? base.ResolveFieldMetadata(valueIds) : [];
    }

    public override ViewNode BuildView()
    {
        if (LargeLegacyView) return new SectionNode(Title, LegacyNodes());
        Bind();
        var children = new List<ViewNode>
        {
            new FieldReferenceNode("quantity") { Label = "Named projection", Description = "Context" },
            new FieldReferenceNode("quantity") { Label = "Second projection" },
            new FieldReferenceNode("total"), new FieldReferenceNode("missing") { Label = "Missing quantity" },
            SectionNode.Lazy("More", "more"),
            new QueryableTableNode("items", "Items", Collections[0].Columns, 1),
            new ActionNode("delete", "Delete canonical field"),
        };
        if (_exists && ShowCanonical) children.Add(Field("quantity", "Canonical quantity") with
        {
            Confidence = Confidence.Inferred, Warning = "Unverified gameplay meaning.", Description = "Canonical explanation",
        });
        if (ReferencesOnlyInPages) children.RemoveAll(node => node is FieldReferenceNode);
        return new SectionNode(Title, children);
    }

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        if (lazyId != "more") throw new KeyNotFoundException();
        Expansions++;
        if (LargeLegacyView) return LegacyNodes();
        return [new TabsNode([new("Projection tab", [Table()])])];
    }

    private static ViewNode[] LegacyNodes() => Enumerable.Range(0, 100_001)
        .Select(index => (ViewNode)SectionNode.Lazy("Item", "legacy/" + index)).ToArray();

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
    {
        if (collectionId != "items") throw new KeyNotFoundException();
        Queries++;
        return new(collectionId, Collections[0].Columns, Table().Rows, 1, 1, query.Offset, query.Limit);
    }

    private static TableNode Table() => new([new("value", "Value")],
        [new("row", new Dictionary<string, ViewNode> { ["value"] = new FieldReferenceNode("quantity") { Label = "Table quantity" } })]);

    public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
        actionId == "delete" ? Structural(() => _exists = false) : throw new KeyNotFoundException();

    protected override DocumentMemento CaptureSnapshot() => DocumentMemento.Snapshot((_quantity, _exists), sizeof(int) + sizeof(bool));
    protected override void LoadSnapshot(DocumentMemento snapshot) =>
        (_quantity, _exists) = ((SnapshotMemento<(int, bool)>)snapshot).State;

    public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new byte[] { (byte)_quantity, _exists ? (byte)1 : (byte)0 })];
}
