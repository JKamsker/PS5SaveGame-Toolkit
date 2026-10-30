using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Sessions;

/// <summary>One bounded view snapshot; does not publish baseline or session state.</summary>
internal sealed class SemanticSnapshotCapture(DocumentSessionOptions limits)
{
    private const int MaxVisitedNodes = 100_000;
    private const int MaxDepth = 128;
    public SemanticSnapshot Capture(ISaveDocument document, Action<FieldNode> index, Action<string> expandedLazy, CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, SemanticField>(StringComparer.Ordinal);
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        var activeLazy = new HashSet<string>(StringComparer.Ordinal);
        long bytes = 0;
        var visited = 0;
        var complete = true;
        var queryTraversal = new SemanticQueryableTraversal(document, limits, cancellationToken);
        var queryCut = false;
        var queryTables = new List<(QueryableTableNode Table, string[] Path, int Depth)>();

        void Children(IEnumerable<ViewNode> children, string[] parent, int depth,
            SemanticCollectionTarget? target, string? rowLabel)
        {
            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (visited >= MaxVisitedNodes || bytes >= limits.MaxOriginalBytes)
                {
                    complete = false;
                    queryCut |= target is not null;
                    break;
                }

                Visit(child, parent, depth + 1, target, rowLabel);
            }
        }

        void Visit(ViewNode node, string[] path, int depth, SemanticCollectionTarget? target = null, string? rowLabel = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++visited > MaxVisitedNodes || depth > MaxDepth || bytes >= limits.MaxOriginalBytes)
            {
                complete = false;
                queryCut |= target is not null;
                return;
            }

            switch (node)
            {
                case FieldNode f:
                    if (fields.ContainsKey(f.ValueId)) break;
                    var field = new SemanticField(f.ValueId, rowLabel is null ? f.Label : $"{rowLabel} / {f.Label}", path, f.Value, f.Spec.ReadOnly, target,
                        Spec: f.Spec);
                    if (!SemanticSnapshot.TryEstimate(field, limits.MaxOriginalBytes - bytes, out var cost))
                    {
                        complete = false;
                        queryCut |= target is not null;
                        break;
                    }

                    index(f);
                    bytes += cost;
                    fields[f.ValueId] = RequiresSpecSnapshot(f.Spec)
                        ? field with { Spec = FieldSpecMetadata.Snapshot(f.Spec) }
                        : field;
                    break;
                case SectionNode s:
                    var sectionPath = path.Append("section:" + (s.LazyId ?? s.Title)).ToArray();
                    if (s.LazyId is { } lazy)
                    {
                        if (expanded.Add(lazy))
                        {
                            activeLazy.Add(lazy);
                            try
                            {
                                var children = document.ExpandNode(lazy);
                                expandedLazy(lazy);
                                Children(children, sectionPath, depth, target, rowLabel);
                            }
                            finally { activeLazy.Remove(lazy); }
                        }
                        // F8 search projections can link the canonical lazy subtree already
                        // reviewed above. Reuse it, but never hide a recursive expansion cycle.
                        else if (target is null || activeLazy.Contains(lazy)) complete = false;
                    }
                    else Children(s.Children, sectionPath, depth, target, rowLabel);
                    break;
                case TabsNode tabs:
                    foreach (var tab in tabs.Tabs)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (++visited > MaxVisitedNodes) { complete = false; queryCut |= target is not null; break; }
                        Children(tab.Children, path.Append("tab:" + tab.Title).ToArray(), depth, target, rowLabel);
                    }
                    break;
                case TableNode table:
                    foreach (var row in table.Rows)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (++visited > MaxVisitedNodes || bytes >= limits.MaxOriginalBytes) { complete = false; queryCut |= target is not null; break; }
                        Children(row.Cells.Values, path.Append("row:" + row.Key).ToArray(), depth, target, rowLabel);
                    }

                    break;
                case QueryableTableNode table:
                    queryTables.Add((table, path, depth));
                    break;
            }
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Visit(document.BuildView(), [], 0);
            // Preserve legacy eager/lazy field precedence and ordering, even when an F8 alias
            // appears first in the view. F8 cells can themselves expose further collections.
            for (var i = 0; i < queryTables.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (table, path, depth) = queryTables[i];
                if (queryCut || bytes >= limits.MaxOriginalBytes || visited >= MaxVisitedNodes)
                {
                    queryTraversal.MarkTruncated(table, "view memory or traversal cap");
                    break;
                }

                queryTraversal.Visit(table, (row, rowTarget, label) =>
                {
                    // Navigation and path belong to the row, not to each individual cell.
                    var rowPath = path.Append("row:" + rowTarget.RowKey).ToArray();
                    foreach (var cell in row.Cells.Values)
                    {
                        Visit(cell, rowPath, depth + 1, rowTarget, label);
                        if (queryCut) return false;
                    }

                    return true;
                });
            }
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            complete = false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new SemanticSnapshot(fields, complete && queryTraversal.Warnings.Count == 0, bytes)
        { QueryWarnings = queryTraversal.Warnings };
    }

    private static bool RequiresSpecSnapshot(FieldSpec spec) => spec is EnumSpec or FlagsSpec or NullableSpec or UnionSpec;

}

internal sealed class SemanticSnapshot(Dictionary<string, SemanticField> fields, bool complete, long bytes)
{
    public Dictionary<string, SemanticField> Fields { get; } = fields;
    public bool Complete { get; set; } = complete;
    public long Bytes { get; set; } = bytes;
    internal long OmittedCount { get; set; }
    internal bool OmittedCountIsLowerBound { get; set; }
    public IReadOnlyList<string> QueryWarnings { get; set; } = [];
    internal SemanticSnapshot Clone()
    {
        var copy = new SemanticSnapshot(Fields.ToDictionary(pair => pair.Key,
            pair => pair.Value with { Path = pair.Value.Path.ToArray() }, StringComparer.Ordinal), Complete, Bytes)
        { QueryWarnings = QueryWarnings.ToArray(), OmittedCount = OmittedCount,
            OmittedCountIsLowerBound = OmittedCountIsLowerBound };
        return copy;
    }

    internal void ReplaceWith(SemanticSnapshot source)
    {
        Fields.Clear();
        foreach (var (id, field) in source.Fields)
            Fields.Add(id, field with { Path = field.Path.ToArray() });
        Complete = source.Complete;
        Bytes = source.Bytes;
        OmittedCount = source.OmittedCount;
        OmittedCountIsLowerBound = source.OmittedCountIsLowerBound;
        QueryWarnings = source.QueryWarnings.ToArray();
    }

    internal void Merge(SemanticSnapshot source, long maxBytes)
    {
        Complete &= source.Complete;
        QueryWarnings = [.. QueryWarnings, .. source.QueryWarnings];
        OmittedCount += source.OmittedCount;
        OmittedCountIsLowerBound |= source.OmittedCountIsLowerBound;
        foreach (var field in source.Fields.Values)
        {
            if (Fields.ContainsKey(field.ValueId)) continue;
            var cost = Estimate(field);
            if (cost > maxBytes - Bytes) { Complete = false; continue; }
            Bytes += cost;
            Fields.Add(field.ValueId, field);
        }
    }

    internal static long Estimate(SemanticField field) =>
        TryEstimate(field, long.MaxValue, out var bytes) ? bytes : long.MaxValue;

    internal static bool TryEstimate(SemanticField field, long limit, out long bytes) =>
        SemanticMetadataBudget.TryEstimate(field, limit, out bytes);

}
