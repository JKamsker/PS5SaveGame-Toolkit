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
    private SemanticSnapshot? _original;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private bool _structureChanged;
    private bool _unreviewedReportedChanges;
    private readonly SemanticSnapshotCapture _capture;
    private readonly long _maxBytes;

    // Preflight can populate the baseline, but cancellation must leave it uncached.
    public Action CancellationRollback()
    {
        var original = _original;
        return () => _original = original;
    }

    public SemanticReview(DocumentSessionOptions limits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxReviewQueryRows);
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxReviewQueryPages);
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxReviewQueryBytes);
        _capture = new SemanticSnapshotCapture(limits);
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
            var cost = SemanticSnapshot.Estimate(field);
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

    public void EnsureOriginal(Func<ISaveDocument> openOriginal) => EnsureOriginal(openOriginal, CancellationToken.None);

    public void EnsureOriginal(Func<ISaveDocument> openOriginal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_original is not null) return;
        try
        {
            // Expanding a lazy view can register aliases and influence legacy actions. Never
            // perform the baseline walk on the live document, even before its first mutation.
            using var original = openOriginal();
            _original = _capture.Capture(original, static _ => { }, static _ => { }, cancellationToken);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            _original = new SemanticSnapshot(new Dictionary<string, SemanticField>(StringComparer.Ordinal), complete: false, bytes: 0);
        }
    }

    public SemanticChangeLog Query(Func<ISaveDocument> openOriginal, ISaveDocument document,
        Action<FieldNode> index, Action<string> expandedLazy, long revision, CancellationToken cancellationToken)
    {
        var previous = _original;
        try
        {
            EnsureOriginal(openOriginal, cancellationToken);
            return Query(document, index, expandedLazy, revision, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _original = previous;
            throw;
        }
    }

    public SemanticChangeLog Query(ISaveDocument document, Action<FieldNode> index, Action<string> expandedLazy, long revision)
        => Query(document, index, expandedLazy, revision, CancellationToken.None);

    private SemanticChangeLog Query(ISaveDocument document, Action<FieldNode> index, Action<string> expandedLazy,
        long revision, CancellationToken cancellationToken)
    {
        var original = _original ?? throw new InvalidOperationException("Capture the original before querying changes.");
        var current = _capture.Capture(document, index, expandedLazy, cancellationToken);
        foreach (var id in _reported.Where(id => !current.Fields.ContainsKey(id)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var field = original.Fields[id] with { Value = document.GetValue(id) };
                var cost = SemanticSnapshot.Estimate(field);
                if (cost > _maxBytes - current.Bytes) { current.Complete = false; continue; }
                current.Bytes += cost;
                current.Fields.Add(id, field);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                current.Complete = false;
            }
        }
        var changes = SemanticSnapshotComparison.Compare(original.Fields, original.Complete,
            current.Fields, current.Complete, document, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

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


}
