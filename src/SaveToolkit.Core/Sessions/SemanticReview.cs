using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Core.Editing;

namespace SaveToolkit.Core.Sessions;

internal sealed record SemanticField(string ValueId, string Label, string[] Path, FieldValue Value, bool ReadOnly,
    SemanticCollectionTarget? Target = null, string? CanonicalValueId = null,
    FieldBindingKind BindingKind = FieldBindingKind.Primary);

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
    private readonly List<string> _captureDiagnostics = [];
    private readonly SemanticSnapshotCapture _capture;
    private readonly long _maxBytes;

    // Preflight can populate the baseline, but cancellation must leave it uncached.
    public Action CancellationRollback()
    {
        var original = _original?.Clone();
        var targeted = _targeted.ToArray();
        var targetedOnly = _targetedOnly;
        var diagnostics = _captureDiagnostics.ToArray();
        return () =>
        {
            _original = original?.Clone();
            _targeted.Clear();
            _targeted.UnionWith(targeted);
            _targetedOnly = targetedOnly;
            _captureDiagnostics.Clear();
            _captureDiagnostics.AddRange(diagnostics);
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
                    if (CaptureTargeted(original, snapshot, ids, revokeOnFailure: false, cancellationToken))
                        _targeted.UnionWith(ids);
                    var walked = _capture.Capture(original, static _ => { }, static _ => { }, cancellationToken);
                    if (walked.Complete)
                        snapshot.Merge(walked, _maxBytes);
                    else
                    {
                        snapshot.Complete = false;
                        snapshot.QueryWarnings = [.. snapshot.QueryWarnings, .. walked.QueryWarnings,
                            "Legacy original view coverage is incomplete; targeted values do not prove whole-document coverage."];
                        _targetedOnly = true;
                    }
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
            if (CaptureTargeted(original, existing, ids, revokeOnFailure: false, cancellationToken))
            {
                _targeted.UnionWith(ids);
                _captureDiagnostics.Clear();
            }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _captureDiagnostics.Add($"Targeted metadata capture failed: {ex.Message}");
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
        ApplyCanonicalMetadata(document, current, cancellationToken);
        if (_targetedOnly)
        {
            var walkWarnings = current.QueryWarnings.ToArray();
            current.Fields.Clear();
            current.Bytes = 0;
            current.Complete = false;
            current.QueryWarnings = [.. walkWarnings,
                "Legacy current view coverage is incomplete; targeted values do not prove whole-document coverage."];
        }
        CaptureTargeted(document, current, _targeted, revokeOnFailure: true, cancellationToken);
        ValidateTargetedAuthority(original, current);
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

        var complete = original.Complete && current.Complete && !_unreviewedReportedChanges
            && _captureDiagnostics.Count == 0;
        IReadOnlyList<string> warnings = complete ? [] : ["Semantic review is incomplete: the original could not be opened, a view exceeded its memory or traversal limit, a lazy section could not be read, or an internal field could not be matched after a structural change. Only proven changes are listed."];
        return new SemanticChangeLog(changes, complete,
            [.. warnings, .. _captureDiagnostics, .. original.QueryWarnings.Concat(current.QueryWarnings).Distinct(StringComparer.Ordinal)], revision);
    }

    private static void ValidateTargetedAuthority(SemanticSnapshot original, SemanticSnapshot current)
    {
        foreach (var (id, before) in original.Fields)
        {
            if (before.CanonicalValueId is null || !current.Fields.TryGetValue(id, out var after)) continue;
            if (string.Equals(before.CanonicalValueId, after.CanonicalValueId, StringComparison.Ordinal)
                && before.BindingKind == after.BindingKind && before.ReadOnly == after.ReadOnly) continue;
            current.Fields.Remove(id);
            current.Complete = false;
            current.QueryWarnings = [.. current.QueryWarnings,
                $"Targeted metadata coverage is incomplete: authority changed for '{id}'; revert is unavailable."];
        }
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

    private bool CaptureTargeted(ISaveDocument document, SemanticSnapshot snapshot, IEnumerable<string> valueIds,
        bool revokeOnFailure, CancellationToken cancellationToken)
    {
        var ids = valueIds.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return true;
        if (document is not IFieldMetadataDocument)
        {
            snapshot.Complete = false;
            snapshot.QueryWarnings = [.. snapshot.QueryWarnings, "Targeted metadata is unavailable for this document."];
            return false;
        }

        var working = snapshot.Clone();
        try
        {
            CaptureTargetedCore(document, working, ids, cancellationToken);
            snapshot.ReplaceWith(working);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (revokeOnFailure)
                foreach (var id in ids) snapshot.Fields.Remove(id);
            snapshot.Complete = false;
            snapshot.QueryWarnings = [.. snapshot.QueryWarnings,
                $"Targeted metadata coverage is incomplete: {ex.Message}"];
            return false;
        }
    }

    private void CaptureTargetedCore(ISaveDocument document, SemanticSnapshot snapshot, IReadOnlyList<string> ids,
        CancellationToken cancellationToken)
    {
        var index = new FieldMetadataIndex(document);
        foreach (var batch in ids.Chunk(FieldMetadataLimits.MaxBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                if (snapshot.Fields.TryGetValue(field.ValueId, out var existing))
                {
                    snapshot.Fields[field.ValueId] = existing with { ReadOnly = field.Spec.ReadOnly };
                    continue;
                }

                var semantic = new SemanticField(field.ValueId, field.Label,
                    field.Path?.ToArray() ?? [], Value: document.GetValue(field.ValueId),
                    ReadOnly: field.Spec.ReadOnly,
                    CanonicalValueId: string.IsNullOrEmpty(field.CanonicalValueId) ? field.ValueId : field.CanonicalValueId,
                    BindingKind: field.BindingKind);
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
    }

    private static void ApplyCanonicalMetadata(ISaveDocument document, SemanticSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (document is not IFieldMetadataDocument || snapshot.Fields.Count == 0) return;
        var index = new FieldMetadataIndex(document);
        foreach (var batch in snapshot.Fields.Keys.Chunk(FieldMetadataIndex.BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                index.ResolveAll(batch, cancellationToken);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                snapshot.Complete = false;
                snapshot.QueryWarnings = [.. snapshot.QueryWarnings,
                    $"Canonical metadata coverage is incomplete: {ex.Message}"];
                continue;
            }

            foreach (var id in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (snapshot.Fields[id] is not { } semantic || index.Cached(id) is not { } metadata) continue;
                snapshot.Fields[id] = semantic with
                {
                    ReadOnly = metadata.Spec.ReadOnly,
                    CanonicalValueId = string.IsNullOrEmpty(metadata.CanonicalValueId) ? id : metadata.CanonicalValueId,
                    BindingKind = metadata.BindingKind,
                };
            }
        }
    }

}
