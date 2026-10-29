using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Sessions;

internal sealed record SemanticField(string ValueId, string Label, string[] Path, FieldValue Value, bool ReadOnly,
    SemanticCollectionTarget? Target = null);

/// <summary>
/// A separate original-value baseline survives history eviction. Both baseline and current walks
/// are bounded, including lazy expansion and navigation metadata. Partial coverage is never
/// interpreted as proof that a field was added or removed.
/// </summary>
internal sealed class SemanticReview
{
    private const int MaxVisitedNodes = 100_000;
    private const int MaxDepth = 128;
    private const int FieldOverheadBytes = 256;
    private Snapshot? _original;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private bool _structureChanged;
    private bool _unreviewedReportedChanges;
    private readonly DocumentSessionOptions _limits;
    private readonly long _maxBytes;

    public SemanticReview(DocumentSessionOptions limits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxReviewQueryRows);
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxReviewQueryPages);
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxReviewQueryBytes);
        _limits = limits;
        _maxBytes = limits.MaxOriginalBytes;
    }

    public void Observe(IReadOnlyList<ValueChange> changes, bool structural, Func<string, bool> indexed)
    {
        if (_original is not { } original) return;
        foreach (var change in changes)
        {
            if (original.Fields.ContainsKey(change.ValueId)) continue;
            // OldValue proves the original for previously unexposed fields only before any
            // structural mutation could have created that identity.
            if (structural || _structureChanged)
            {
                // Newly exposed fields are reviewed as additions by the view walk. Unknown
                // internal identities after structure changes cannot be matched to the original.
                if (!structural && !indexed(change.ValueId)) _unreviewedReportedChanges = true;
                continue;
            }

            var field = new SemanticField(change.ValueId, change.ValueId, [], change.OldValue, ReadOnly: true);
            var cost = Estimate(field);
            if (cost > _maxBytes - original.Bytes)
            {
                original.Complete = false;
                continue;
            }

            original.Bytes += cost;
            original.Fields.Add(field.ValueId, field);
            _reported.Add(field.ValueId);
        }

        _structureChanged |= structural;
    }

    public void EnsureOriginal(Func<ISaveDocument> openOriginal)
    {
        if (_original is not null) return;
        try
        {
            // Expanding a lazy view can register aliases and influence legacy actions. Never
            // perform the baseline walk on the live document, even before its first mutation.
            using var original = openOriginal();
            _original = Capture(original, static _ => { }, static _ => { });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _original = new Snapshot(new Dictionary<string, SemanticField>(StringComparer.Ordinal), complete: false, bytes: 0);
        }
    }

    public SemanticChangeLog Query(ISaveDocument document, Action<FieldNode> index, Action<string> expandedLazy, long revision)
    {
        var original = _original ?? throw new InvalidOperationException("Capture the original before querying changes.");
        var current = Capture(document, index, expandedLazy);
        foreach (var id in _reported.Where(id => !current.Fields.ContainsKey(id)))
        {
            try
            {
                var field = original.Fields[id] with { Value = document.GetValue(id) };
                var cost = Estimate(field);
                if (cost > _maxBytes - current.Bytes) { current.Complete = false; continue; }
                current.Bytes += cost;
                current.Fields.Add(id, field);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                current.Complete = false;
            }
        }
        var changes = new List<SemanticChange>();
        foreach (var id in original.Fields.Keys.Union(current.Fields.Keys, StringComparer.Ordinal))
        {
            var before = original.Fields.GetValueOrDefault(id);
            var after = current.Fields.GetValueOrDefault(id);
            if ((before is null && !original.Complete) || (after is null && !current.Complete)
                || (before is not null && after is not null && before.Value == after.Value))
            {
                continue;
            }

            var field = after ?? before!;
            changes.Add(new SemanticChange(id, field.Label, field.Path,
                before?.Value ?? FieldValue.Null, after?.Value ?? FieldValue.Null,
                before is not null, after is not null, before is not null && after is { ReadOnly: false } && document.CanEdit)
            { Target = after?.Target });
        }

        var complete = original.Complete && current.Complete && !_unreviewedReportedChanges;
        IReadOnlyList<string> warnings = complete ? [] : ["Semantic review is incomplete: the original could not be opened, a view exceeded its memory or traversal limit, a lazy section could not be read, or an internal field could not be matched after a structural change. Only proven changes are listed."];
        return new SemanticChangeLog(changes, complete,
            [.. warnings, .. original.QueryWarnings.Concat(current.QueryWarnings).Distinct(StringComparer.Ordinal)], revision);
    }

    public bool TryOriginal(string id, out FieldValue value)
    {
        if (_original?.Fields.GetValueOrDefault(id) is { } field)
        {
            value = field.Value;
            return true;
        }

        value = default;
        return false;
    }

    private Snapshot Capture(ISaveDocument document, Action<FieldNode> index, Action<string> expandedLazy)
    {
        var fields = new Dictionary<string, SemanticField>(StringComparer.Ordinal);
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        var activeLazy = new HashSet<string>(StringComparer.Ordinal);
        long bytes = 0;
        var visited = 0;
        var complete = true;
        var queryTraversal = new SemanticQueryableTraversal(document, _limits);
        var queryCut = false;
        var queryTables = new List<(QueryableTableNode Table, string[] Path, int Depth)>();

        void Visit(ViewNode node, string[] path, int depth, SemanticCollectionTarget? target = null, string? rowLabel = null)
        {
            if (++visited > MaxVisitedNodes || depth > MaxDepth || bytes >= _maxBytes)
            {
                complete = false;
                queryCut |= target is not null;
                return;
            }

            void Children(IEnumerable<ViewNode> children, string[] parent)
            {
                foreach (var child in children)
                {
                    if (visited >= MaxVisitedNodes || bytes >= _maxBytes)
                    {
                        complete = false;
                        queryCut |= target is not null;
                        break;
                    }

                    Visit(child, parent, depth + 1, target, rowLabel);
                }
            }

            switch (node)
            {
                case FieldNode f:
                    if (fields.ContainsKey(f.ValueId)) break;
                    var field = new SemanticField(f.ValueId, rowLabel is null ? f.Label : $"{rowLabel} / {f.Label}", path, f.Value, f.Spec.ReadOnly, target);
                    var cost = Estimate(field);
                    if (cost > _maxBytes - bytes)
                    {
                        complete = false;
                        queryCut |= target is not null;
                        break;
                    }

                    bytes += cost;
                    fields[f.ValueId] = field;
                    index(f);
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
                                Children(children, sectionPath);
                            }
                            finally { activeLazy.Remove(lazy); }
                        }
                        // F8 search projections can link the canonical lazy subtree already
                        // reviewed above. Reuse it, but never hide a recursive expansion cycle.
                        else if (target is null || activeLazy.Contains(lazy)) complete = false;
                    }
                    else Children(s.Children, sectionPath);
                    break;
                case TabsNode tabs:
                    foreach (var tab in tabs.Tabs)
                    {
                        if (++visited > MaxVisitedNodes) { complete = false; queryCut |= target is not null; break; }
                        Children(tab.Children, path.Append("tab:" + tab.Title).ToArray());
                    }
                    break;
                case TableNode table:
                    foreach (var row in table.Rows)
                    {
                        if (++visited > MaxVisitedNodes || bytes >= _maxBytes) { complete = false; queryCut |= target is not null; break; }
                        Children(row.Cells.Values, path.Append("row:" + row.Key).ToArray());
                    }

                    break;
                case QueryableTableNode table:
                    queryTables.Add((table, path, depth));
                    break;
            }
        }

        try
        {
            Visit(document.BuildView(), [], 0);
            // Preserve legacy eager/lazy field precedence and ordering, even when an F8 alias
            // appears first in the view. F8 cells can themselves expose further collections.
            for (var i = 0; i < queryTables.Count; i++)
            {
                var (table, path, depth) = queryTables[i];
                if (queryCut || bytes >= _maxBytes || visited >= MaxVisitedNodes)
                {
                    queryTraversal.MarkTruncated(table, "view memory or traversal cap");
                    break;
                }

                queryTraversal.Visit(table, (cell, rowTarget, label) =>
                {
                    Visit(cell, path.Append("row:" + rowTarget.RowKey).ToArray(), depth + 1, rowTarget, label);
                    return !queryCut;
                });
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            complete = false;
        }

        return new Snapshot(fields, complete && queryTraversal.Warnings.Count == 0, bytes)
        { QueryWarnings = queryTraversal.Warnings };
    }

    private static long Estimate(SemanticField field) => FieldOverheadBytes
        + 2L * (field.ValueId.Length + field.Label.Length + field.Value.ToString().Length + field.Path.Sum(p => p.Length)
            + (field.Target is { } target ? target.CollectionId.Length + target.RowKey.Length + (target.Query.Cursor?.Length ?? 0) : 0));

    private sealed class Snapshot(Dictionary<string, SemanticField> fields, bool complete, long bytes)
    {
        public Dictionary<string, SemanticField> Fields { get; } = fields;
        public bool Complete { get; set; } = complete;
        public long Bytes { get; set; } = bytes;
        public IReadOnlyList<string> QueryWarnings { get; init; } = [];
    }
}
