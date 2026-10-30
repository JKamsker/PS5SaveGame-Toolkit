using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.UnrealGvas;

/// <summary>Adds F8 navigation without changing the raw document composed by Hogwarts.</summary>
internal sealed class GvasQueryableDocument : ISaveDocument, IQueryableDocument, IFieldMetadataDocument
{
    private readonly UnrealGvasDocument _raw;
    private GvasNamedViews _views;
    private readonly UnrealGvasSchema _schema;
    private readonly IReferenceContext _references;

    public GvasQueryableDocument(string path, GvasEnvelope envelope, UnrealGvasSchema schema, IReferenceContext references)
    {
        _raw = new(path, envelope, references);
        _schema = schema;
        _references = references;
        _views = new(envelope.File, schema, _raw.ActorStream, references);
    }

    public string Title => _raw.Title;
    public bool CanEdit => _raw.CanEdit;
    public IReadOnlyList<QueryableCollection> Collections => _views.Collections;
    public ViewNode BuildView() => new TabsNode([.. ((TabsNode)_raw.BuildView()).Tabs, .. _views.Tabs]);
    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) => _views.Query(collectionId, query);
    public IReadOnlyList<ViewNode> ExpandNode(string lazyId) => _raw.ExpandNode(lazyId);
    public FieldValue GetValue(string valueId) => _raw.GetValue(valueId);
    public IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds) => _raw.ResolveFieldMetadata(valueIds);
    public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) => _raw.ApplyEdits(edits);
    public ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        var outcome = _raw.InvokeAction(actionId, parameters);
        if (outcome.StructureChanged) _views = new GvasNamedViews(_raw.CurrentFile, _schema, _raw.ActorStream, _references);
        return outcome;
    }
    public IReadOnlyList<FileOutput> Serialize() => _raw.Serialize();
    public AssetContent? GetAsset(string assetId) => _raw.GetAsset(assetId);
    public ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count) => _raw.ReadBlob(blobId, offset, count);
    public DocumentMemento RestoreMemento(DocumentMemento memento)
    {
        var result = _raw.RestoreMemento(memento);
        _views = new GvasNamedViews(_raw.CurrentFile, _schema, _raw.ActorStream, _references);
        return result;
    }
    public void Dispose() => _raw.Dispose();
}
