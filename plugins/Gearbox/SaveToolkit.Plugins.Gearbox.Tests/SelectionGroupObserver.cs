using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox.Tests;

/// <summary>
/// Observes group lifetimes without changing dispatch or staging. Between synchronous host calls,
/// candidate/original-review groups have been disposed and only the published live model remains.
/// This lets byte assertions inspect that model, even when a clean session exports no delta.
/// </summary>
internal sealed class SelectionGroupObserver : IGamePlugin, IDocumentGroupPlugin
{
    // Require explicit construction so default plugin discovery cannot register this test observer.
    internal SelectionGroupObserver() { }

    private readonly GearboxPlugin _plugin = new();
    private readonly HashSet<ObservedGroup> _open = [];
    internal Func<EditOutcome, EditOutcome>? TransformActionOutcome { get; set; }
    internal GearboxGroupDocument Current => Assert.Single(_open).Document;
    public PluginInfo Info => _plugin.Info;
    public ProbeResult Probe(ISaveFile file) => _plugin.Probe(file);
    public ISaveDocument Open(ISaveFile file) => _plugin.Open(file);
    public ISaveDocument Open(ISaveFile file, IReferenceContext references) => _plugin.Open(file, references);
    public IReadOnlyList<DocumentGroupProposal> ProposeGroups(ISaveFile anchor) => _plugin.ProposeGroups(anchor);
    public IReadOnlyList<FieldError> ValidateGroup(DocumentGroupInput input, IReferenceContext references) => _plugin.ValidateGroup(input, references);

    public IGroupedSaveDocument OpenGroup(DocumentGroupInput input, IReferenceContext references)
    {
        var group = new ObservedGroup(this, (GearboxGroupDocument)_plugin.OpenGroup(input, references));
        _open.Add(group);
        return group;
    }

    private sealed class ObservedGroup(SelectionGroupObserver owner, GearboxGroupDocument document)
        : IGroupedSaveDocument, IQueryableDocument, IActionEffectDocument, IGroupedFieldContext
    {
        internal GearboxGroupDocument Document => document;
        public string Title => document.Title;
        public bool CanEdit => document.CanEdit;
        public IReadOnlyList<QueryableCollection> Collections => document.Collections;
        public ViewNode BuildView() => document.BuildView();
        public IReadOnlyList<ViewNode> ExpandNode(string lazyId) => document.ExpandNode(lazyId);
        public FieldValue GetValue(string valueId) => document.GetValue(valueId);
        public IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> ids) => document.ResolveFieldMetadata(ids);
        public IReadOnlyList<DocumentGroupFieldContext> ResolveFieldContext(IReadOnlyList<string> ids) => document.ResolveFieldContext(ids);
        public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) => document.Query(collectionId, query);
        public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) => document.ApplyEdits(edits);
        public ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) => document.InvokeAction(actionId, parameters);
        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
            document.PlanAction(actionId, parameters, scope);
        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan) =>
            owner.TransformActionOutcome is { } transform
                ? transform(document.ApplyActionPlan(actionId, parameters, scope, plan))
                : document.ApplyActionPlan(actionId, parameters, scope, plan);
        public IReadOnlyList<FileOutput> Serialize() => document.Serialize();
        public DocumentMemento CaptureMemento() => document.CaptureMemento();
        public DocumentMemento RestoreMemento(DocumentMemento memento) => document.RestoreMemento(memento);
        public AssetContent? GetAsset(string assetId) => document.GetAsset(assetId);
        public ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count) => document.ReadBlob(blobId, offset, count);
        public void Dispose()
        {
            try { document.Dispose(); }
            finally { owner._open.Remove(this); }
        }
    }
}
