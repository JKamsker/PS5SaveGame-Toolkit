using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

internal sealed class FieldReferenceBudgetDocument(string text, string? warning = null) : BoundSaveDocument, IFieldMetadataDocument, IQueryableDocument
{
    public override string Title => "Reference budget";
    public IReadOnlyList<QueryableCollection> Collections => [new("items", "Items", [new("value", "Value")], 200)];
    public override ViewNode BuildView() => new SectionNode(Title,
        [SectionNode.Lazy("Expanded references", "more"), new QueryableTableNode("items", "Items", Collections[0].Columns, 200)]);
    public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
    {
        RegisterField(new FieldMetadata("text", "Full canonical value", new TextSpec { ReadOnly = true })
            { Presentation = new() { Confidence = Confidence.Unknown, Warning = warning } }, () => FieldValue.Of(text));
        return base.ResolveFieldMetadata(valueIds);
    }
    public static ViewNode[] References(int count) => Enumerable.Range(0, count)
        .Select(_ => (ViewNode)new FieldReferenceNode("text")).ToArray();
    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) => lazyId == "more" ? References(200) : throw new KeyNotFoundException();
    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) => new(collectionId,
        Collections[0].Columns, References(query.Limit).Select((node, index) => new TableRow("row/" + index,
            new Dictionary<string, ViewNode> { ["value"] = node })).ToArray(), 200, 200, query.Offset, query.Limit);
    public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new byte[] { 1 })];
}

internal sealed class FieldReferenceBudgetPlugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("test.reference-budget", "Reference budget", "1.0.0");
    public ProbeResult Probe(ISaveFile file) => ProbeResult.Match(ProbeScore.Title, "Synthetic fixture");
    public ISaveDocument Open(ISaveFile file) => new FieldReferenceBudgetDocument(new string('"', 4000));
}
