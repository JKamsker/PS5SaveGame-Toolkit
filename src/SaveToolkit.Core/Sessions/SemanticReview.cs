using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Core.Editing;

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
    private readonly HashSet<string> _targeted = new(StringComparer.Ordinal);
    private bool _targetedOnly;
    private bool _capturingOriginal;
    private readonly SemanticSnapshotCapture _capture;
    private readonly long _maxBytes;

    // Preflight can populate the baseline, but cancellation must leave it uncached.
    public Action CancellationRollback()
    {
        var original = _original?.Clone();
        var targeted = _targeted.ToArray();
        var targetedOnly = _targetedOnly;
        return () =>
        {
            _original = original?.Clone();
            _targeted.Clear();
            _targeted.UnionWith(targeted);
            _targetedOnly = targetedOnly;
        };
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

    /// <summary>
    /// Captures explicitly affected IDs from a fresh original document without walking its view.
    /// Missing descriptors, resolver contradictions and the shared original-byte budget make the
    /// review incomplete; they never become an invented unchanged value.
    /// </summary>
    public void CaptureTargetedOriginals(Func<ISaveDocument> openOriginal, IEnumerable<string> valueIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(openOriginal);
        ArgumentNullException.ThrowIfNull(valueIds);
        var ids = valueIds.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0)
        {
            EnsureOriginal(openOriginal, cancellationToken);
            return;
        }

        if (_original is { } known && ids.All(known.Fields.ContainsKey)) return;

        // Verification callbacks can re-enter a live session while the independent original is
        // opening. Do not recursively open originals; the outer capture owns the baseline.
        if (_capturingOriginal) return;
        _capturingOriginal = true;
        try
        {

            if (_original is null)
            {
            try
            {
                using var original = openOriginal();
                SemanticSnapshot snapshot;
                if (original is IFieldMetadataDocument)
                {
                    // Targeted metadata is authoritative for an unloaded edit. Resolve it before
                    // any legacy view/lazy/query walk so unrelated view failures cannot erase it.
                    snapshot = new SemanticSnapshot(new Dictionary<string, SemanticField>(StringComparer.Ordinal),
                        complete: true, bytes: 0);
                    CaptureTargeted(original, snapshot, ids, cancellationToken);
                    foreach (var id in ids) _targeted.Add(id);
                    var walked = _capture.Capture(original, static _ => { }, static _ => { }, cancellationToken);
                    if (walked.Complete)
                        snapshot.Merge(walked, _maxBytes);
                    else
                        _targetedOnly = true;
                }
                else
                {
                    snapshot = _capture.Capture(original, static _ => { }, static _ => { }, cancellationToken);
                }
                _original = snapshot;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _original = new SemanticSnapshot(new Dictionary<string, SemanticField>(StringComparer.Ordinal), complete: false, bytes: 0)
                { QueryWarnings = [$"Targeted metadata capture failed: {ex.Message}"] };
            }

                return;
            }

            var existing = _original;

            try
            {
            using var original = openOriginal();
            if (original is not IFieldMetadataDocument)
                return;
            foreach (var id in ids) _targeted.Add(id);
            CaptureTargeted(original, existing, ids, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                existing.Complete = false;
                existing.QueryWarnings = [.. existing.QueryWarnings, $"Targeted metadata capture failed: {ex.Message}"];
            }
        }
        finally
        {
            _capturingOriginal = false;
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
        if (_targetedOnly)
        {
            current.Fields.Clear();
            current.Bytes = 0;
            current.Complete = true;
            current.QueryWarnings = [];
        }
        CaptureTargeted(document, current, _targeted, cancellationToken);
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

    private void CaptureTargeted(ISaveDocument document, SemanticSnapshot snapshot, IEnumerable<string> valueIds,
        CancellationToken cancellationToken)
    {
        var ids = valueIds.Where(id => !snapshot.Fields.ContainsKey(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return;
        if (document is not IFieldMetadataDocument) { snapshot.Complete = false; return; }
        var index = new FieldMetadataIndex(document);
        foreach (var batch in ids.Chunk(FieldMetadataLimits.MaxBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var requested = batch.ToHashSet(StringComparer.Ordinal);
                index.ResolveAll(batch, cancellationToken);
                var descriptors = batch.Select(index.Cached).Where(field => field is not null).Cast<FieldMetadata>().ToArray();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var field in descriptors)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!requested.Contains(field.ValueId) || !seen.Add(field.ValueId) || string.IsNullOrEmpty(field.Label)
                        || field.Spec is null || !Enum.IsDefined(field.BindingKind))
                        throw new InvalidOperationException("Invalid targeted field metadata.");
                    if (snapshot.Fields.ContainsKey(field.ValueId)) continue;
                    var semantic = new SemanticField(field.ValueId, field.Label,
                        field.Path?.ToArray() ?? [], Value: document.GetValue(field.ValueId),
                        ReadOnly: field.Spec.ReadOnly);
                    var cost = SemanticSnapshot.Estimate(semantic);
                    if (cost > _maxBytes - snapshot.Bytes)
                    {
                        snapshot.Complete = false;
                        continue;
                    }

                    snapshot.Bytes += cost;
                    snapshot.Fields.Add(field.ValueId, semantic);
                }

                if (seen.Count != requested.Count) snapshot.Complete = false;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                snapshot.Complete = false;
                snapshot.QueryWarnings = [.. snapshot.QueryWarnings, $"Targeted metadata coverage is incomplete: {ex.Message}"];
            }
        }
    }

}
