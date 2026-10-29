using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Api.Performance.Tests;

/// <summary>Counts actual provider calls without changing the plugin's page contents.</summary>
internal sealed class CountingReviewDocument(ISaveDocument inner, bool projectCollections = false, Action? onPage = null)
    : ISaveDocument, IQueryableDocument
{
    private ViewNode? _projection;
    public int Pages { get; private set; }
    public string Title => inner.Title;
    public bool CanEdit => inner.CanEdit;
    public IReadOnlyList<QueryableCollection> Collections => ((IQueryableDocument)inner).Collections;
    public ViewNode BuildView()
    {
        if (!projectCollections) return inner.BuildView();
        var section = (SectionNode)(_projection ??= Project());
        return section with { Children = section.Children.Select(n => n is FieldNode f
            ? f with { Value = inner.GetValue(f.ValueId) } : n).ToArray() };
    }
    public IReadOnlyList<ViewNode> ExpandNode(string lazyId) => inner.ExpandNode(lazyId);
    public FieldValue GetValue(string valueId) => inner.GetValue(valueId);
    public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) => inner.ApplyEdits(edits);
    public IReadOnlyList<FileOutput> Serialize() => inner.Serialize();
    public void Dispose() => inner.Dispose();

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
    {
        Pages++;
        onPage?.Invoke();
        return ((IQueryableDocument)inner).Query(collectionId, query);
    }

    private ViewNode Project()
    {
        var eager = ReviewSampleViews.Walk(inner, inner.BuildView()).OfType<FieldNode>()
            .Where(f => !f.Spec.ReadOnly && f.Label.Contains("Quantity", StringComparison.OrdinalIgnoreCase)).ToArray();
        // This diagnostic projection omits the raw evidence tree; provider pages remain untouched.
        var largest = Collections.MaxBy(c => c.TotalCount)!;
        var collections = Collections.Where(c => c.Id.Contains("inventory", StringComparison.OrdinalIgnoreCase)
                || c.Id.EndsWith("/values", StringComparison.Ordinal) || c.Id == largest.Id)
            .OrderBy(c => c.Id == largest.Id).Select(c => new QueryableTableNode(c.Id, c.Title, c.Columns, c.TotalCount));
        return new SectionNode("F8 measurement projection", [.. eager, .. collections]);
    }
}
