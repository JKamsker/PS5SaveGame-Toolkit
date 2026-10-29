using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

/// <summary>Only successful projections should teach the host these test-only bindings.</summary>
internal sealed class FieldReferenceTransactionPlugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("test.reference-transaction", "Reference transaction", "1.0.0");
    public List<Document> Opened { get; } = [];
    public ProbeResult Probe(ISaveFile file) => ProbeResult.Match(ProbeScore.Title);
    public ISaveDocument Open(ISaveFile file)
    {
        var document = new Document(file.RelativePath);
        Opened.Add(document);
        return document;
    }

    internal sealed class Document(string path) : BoundSaveDocument, IFieldMetadataDocument, IQueryableDocument, IGroupedSaveDocument
    {
        private int _visible = 1;
        private int _hidden = 5;
        private static readonly IntegerSpec Spec = new(0, 10);
        public bool FailRoot { get; set; }
        public bool FaultActive { get; set; }
        public bool AnnotationConflict { get; set; }
        public bool OversizedEnvelope { get; set; }
        public int GoodExpansions { get; private set; }
        public int BadExpansions { get; private set; }
        public int HiddenWrites { get; private set; }
        public int HiddenActions { get; private set; }
        public int Queries { get; private set; }
        public override string Title => "Transactional projections";
        public IReadOnlyList<QueryableCollection> Collections => [new("items", "Items",
            [new("value", "Value") { Description = OversizedEnvelope ? new string('"', 400_000) : null }], 1)];

        private FieldNode Visible() => Field("visible", "Visible", Spec, () => FieldValue.Of(_visible), value => _visible = value.AsInt32())
            with { Warning = "Canonical caution" };

        public override ViewNode BuildView() => new SectionNode(Title,
        [
            Visible(), new FieldReferenceNode("visible"), SectionNode.Lazy("Established", "good"),
            SectionNode.Lazy("Candidate", "bad"), new ActionNode("rebuild", "Rebuild"),
            new QueryableTableNode("items", "Items", Collections[0].Columns, 1),
            .. FailRoot ? FaultNodes() : [],
        ]);

        private ViewNode[] FaultNodes()
        {
            var nodes = new List<ViewNode>
            {
                Field("hidden", "Hidden", Spec, () => FieldValue.Of(_hidden), value => { _hidden = value.AsInt32(); HiddenWrites++; }),
                new ActionNode("hidden-action", "Hidden action"), SectionNode.Lazy("Hidden lazy", "hidden-lazy"),
                new FieldReferenceNode("metadata-only"),
            };
            if (AnnotationConflict)
            {
                nodes.Add(Visible() with { Warning = null });
                nodes.Add(new FieldReferenceNode("visible"));
            }
            else if (!OversizedEnvelope)
            {
                nodes.Add(Field("large", "Large", new TextSpec { ReadOnly = true }, () => FieldValue.Of(new string('"', 4000))));
                nodes.AddRange(Enumerable.Range(0, 200).Select(_ => new FieldReferenceNode("large")));
            }
            return nodes.ToArray();
        }

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            if (!FaultActive) return [];
            RegisterField(new FieldMetadata("visible", "Visible", Spec), () => FieldValue.Of(_visible), value => _visible = value.AsInt32());
            RegisterField(new FieldMetadata("metadata-only", "Metadata only", Spec with { ReadOnly = true }) { Presentation = new() }, () => FieldValue.Of(3));
            return base.ResolveFieldMetadata(valueIds).Where(field => field.ValueId is "visible" or "metadata-only").ToArray();
        }

        public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
        {
            if (lazyId == "bad") { BadExpansions++; return FaultNodes(); }
            if (lazyId != "good") throw new KeyNotFoundException();
            GoodExpansions++;
            return [Field("established", "Established", Spec, () => FieldValue.Of(_visible), value => _visible = value.AsInt32())];
        }

        public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
        {
            Queries++;
            return new(collectionId, Collections[0].Columns,
                [new("row", new Dictionary<string, ViewNode> { ["value"] = new SectionNode("Candidate", FaultNodes()) })],
                1, 1, query.Offset, query.Limit);
        }
        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
        {
            if (actionId == "hidden-action") { HiddenActions++; return new(); }
            if (actionId != "rebuild") throw new KeyNotFoundException();
            return new() { StructureChanged = true };
        }
        public override IReadOnlyList<FileOutput> Serialize() => [new(path, new byte[] { (byte)_visible })];
        public DocumentMemento CaptureMemento() => CaptureSnapshot();
        protected override DocumentMemento CaptureSnapshot() => DocumentMemento.Snapshot((_visible, _hidden), 2 * sizeof(int));
        protected override void LoadSnapshot(DocumentMemento snapshot) =>
            (_visible, _hidden) = ((SnapshotMemento<(int, int)>)snapshot).State;
    }
}
