using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.UnrealGvas;

/// <summary>Adds F8 navigation without changing the raw document composed by Hogwarts.</summary>
internal sealed class GvasQueryableDocument : ISaveDocument, IQueryableDocument
{
    private readonly UnrealGvasDocument _raw;
    private readonly GvasNamedViews _views;

    public GvasQueryableDocument(string path, GvasEnvelope envelope, UnrealGvasSchema schema, IReferenceContext references)
    {
        _raw = new(path, envelope, references);
        _views = new(envelope.File, schema, _raw.ActorStream, references);
    }

    public string Title => _raw.Title;
    public bool CanEdit => _raw.CanEdit;
    public IReadOnlyList<QueryableCollection> Collections => _views.Collections;
    public ViewNode BuildView() => new TabsNode([.. ((TabsNode)_raw.BuildView()).Tabs, .. _views.Tabs]);
    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) => _views.Query(collectionId, query);
    public IReadOnlyList<ViewNode> ExpandNode(string lazyId) => _raw.ExpandNode(lazyId);
    public FieldValue GetValue(string valueId) => _raw.GetValue(valueId);
    public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) => _raw.ApplyEdits(edits);
    public ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) => _raw.InvokeAction(actionId, parameters);
    public IReadOnlyList<FileOutput> Serialize() => _raw.Serialize();
    public AssetContent? GetAsset(string assetId) => _raw.GetAsset(assetId);
    public ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count) => _raw.ReadBlob(blobId, offset, count);
    public void Dispose() => _raw.Dispose();
}
