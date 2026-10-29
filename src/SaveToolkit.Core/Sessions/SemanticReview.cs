using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Core.Editing;

namespace SaveToolkit.Core.Sessions;

internal sealed record SemanticField(string ValueId, string Label, string[] Path, FieldValue Value, bool ReadOnly,
    SemanticCollectionTarget? Target = null, string? CanonicalValueId = null,
    FieldBindingKind BindingKind = FieldBindingKind.Primary, FieldSpec? Spec = null,
    FieldPresentation? Presentation = null);
internal sealed record SemanticReviewState(SemanticSnapshot? Original,
    IReadOnlyList<string> TargetedIds, IReadOnlyList<string> CaptureDiagnostics);

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
    private bool? _originalMetadataAvailable;
    private bool _originalMetadataAttempted;
    private readonly HashSet<string> _originalRetryIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _captureFailures = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failedOmissionIds = new(StringComparer.Ordinal);
    private int _captureOmittedCount;
    private string? _captureOmissionReason;
    private readonly SemanticSnapshotCapture _capture;
    private readonly SemanticTargetedCapture _targetedCapture;
    private readonly long _maxBytes;

    // Preflight can populate the baseline, but cancellation must leave it uncached.
    public Action CancellationRollback()
    {
        var original = _original?.Clone();
        var targeted = _targeted.ToArray();
        var targetedOnly = _targetedOnly;
        var metadataAvailable = _originalMetadataAvailable;
        var metadataAttempted = _originalMetadataAttempted;
        var retryIds = _originalRetryIds.ToArray();
        var diagnostics = _captureFailures.ToArray();
        var failedOmissions = _failedOmissionIds.ToArray();
        var omittedCount = _captureOmittedCount;
        var omissionReason = _captureOmissionReason;
        return () =>
        {
            _original = original?.Clone();
            _targeted.Clear();
            _targeted.UnionWith(targeted);
            _targetedOnly = targetedOnly;
            _originalMetadataAvailable = metadataAvailable;
            _originalMetadataAttempted = metadataAttempted;
            _originalRetryIds.Clear();
            _originalRetryIds.UnionWith(retryIds);
            _captureFailures.Clear();
            foreach (var (id, diagnostic) in diagnostics) _captureFailures[id] = diagnostic;
            _failedOmissionIds.Clear();
            _failedOmissionIds.UnionWith(failedOmissions);
            _captureOmittedCount = omittedCount;
            _captureOmissionReason = omissionReason;
        };
    }
    public SemanticReview(DocumentSessionOptions limits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxReviewQueryRows);
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxReviewQueryPages);
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxReviewQueryBytes);
        _capture = new SemanticSnapshotCapture(limits);
        _targetedCapture = new SemanticTargetedCapture(limits.MaxOriginalBytes);
        _maxBytes = limits.MaxOriginalBytes;
    }
    internal SemanticReviewState CaptureState() => new(
        _original?.Clone(), _targeted.ToArray(), CaptureDiagnostics());
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
            _originalMetadataAvailable = original is IFieldMetadataDocument;
            _original = _capture.Capture(original, static _ => { }, static _ => { }, cancellationToken);
            if (_originalMetadataAvailable == true)
            {
                var metadata = SemanticTargetedAuthority.ApplyCanonicalMetadata(original, _original, _maxBytes, cancellationToken);
                if (!metadata.Applied)
                {
                    _original.Complete = false;
                    _original.QueryWarnings = [.. _original.QueryWarnings, metadata.Diagnostic!];
                    RecordOriginalFailures(metadata.IncompleteIds, metadata.Diagnostic!);
                }
            }
            _originalMetadataAttempted = _originalRetryIds.Count == 0;
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
        var ids = CollectTargetIds(valueIds, out var omittedCount);
        if (omittedCount > 0)
            RecordOmissions(omittedCount, "The bounded targeted metadata aggregate limit was reached.");
        if (ids.Length == 0)
        {
            EnsureOriginal(openOriginal, cancellationToken);
            return;
        }
        if (_original is { } known && _originalMetadataAvailable == false
            && ids.All(known.Fields.ContainsKey)) return;
        if (_original is { } metadataKnown && _originalMetadataAvailable != false
            && ids.All(id => metadataKnown.Fields.TryGetValue(id, out var field) && SemanticOriginalMetadata.HasMetadata(field))
            && metadataKnown.Fields.Values.All(SemanticOriginalMetadata.HasMetadata)) return;
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
                    var outcome = CaptureTargeted(original, snapshot, ids, revokeOnFailure: false, cancellationToken);
                    RegisterOriginalRetryIds(outcome.IncompleteIds);
                    _targeted.UnionWith(outcome.CapturedIds);
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
                    var metadata = SemanticTargetedAuthority.ApplyCanonicalMetadata(original, snapshot, _maxBytes, cancellationToken);
                    if (!metadata.Applied)
                    {
                        snapshot.Complete = false;
                        snapshot.QueryWarnings = [.. snapshot.QueryWarnings, metadata.Diagnostic!];
                        RecordOriginalFailures(metadata.IncompleteIds, metadata.Diagnostic!);
                    }
                    if (metadata.Applied) ClearRecoveredOriginalFailures(snapshot);
                    _originalMetadataAvailable = true;
                    _originalMetadataAttempted = _originalRetryIds.Count == 0;
                }
                else
                {
                    snapshot = _capture.Capture(original, static _ => { }, static _ => { }, cancellationToken);
                    // The runtime type proves this document cannot provide targeted metadata.
                    // Remember that capability result so re-entrant edits reuse the captured
                    // baseline instead of reopening the original during verification.
                    _originalMetadataAvailable = false;
                    _originalMetadataAttempted = true;
                }
                _original = snapshot;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (_originalRetryIds.Count == 0)
                    RecordOriginalFailures(ids, $"Targeted metadata capture failed: {ex.Message}");
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
            {
                _originalMetadataAvailable = false;
                _originalMetadataAttempted = true;
                return;
            }
            _originalMetadataAvailable = true;
            var outcome = CaptureTargeted(original, existing, ids, revokeOnFailure: false, cancellationToken);
            RegisterOriginalRetryIds(outcome.IncompleteIds);
            _targeted.UnionWith(outcome.CapturedIds);
            var metadata = SemanticTargetedAuthority.ApplyCanonicalMetadata(original, existing, _maxBytes, cancellationToken);
            if (!metadata.Applied)
                RecordOriginalFailures(metadata.IncompleteIds, metadata.Diagnostic!);
            else
                ClearRecoveredOriginalFailures(existing);
            _originalMetadataAttempted = _originalRetryIds.Count == 0;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                RecordOriginalFailures(ids, $"Targeted metadata capture failed: {ex.Message}");
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
            if ((!_originalMetadataAttempted || _originalRetryIds.Count > 0)
                && _originalMetadataAvailable != false && _original is { } baseline)
            {
                var metadata = SemanticOriginalMetadata.Enrich(openOriginal, baseline, _maxBytes,
                    _originalRetryIds.ToArray(), ref _capturingOriginal, cancellationToken);
                if (!metadata.Applied)
                    RecordOriginalFailures(metadata.IncompleteIds, metadata.Diagnostic!);
                else
                    ClearRecoveredOriginalFailures(baseline);
                _originalMetadataAttempted = metadata.Applied && _originalRetryIds.Count == 0;
            }
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
        var current = new SemanticSnapshot(new Dictionary<string, SemanticField>(StringComparer.Ordinal), true, 0);
        var targetedCaptured = CaptureTargeted(document, current, _targeted.ToArray(), revokeOnFailure: true, cancellationToken);
        var walked = _capture.Capture(document, index, expandedLazy, cancellationToken);
        if (_targetedOnly)
        {
            current.QueryWarnings = [.. current.QueryWarnings, .. walked.QueryWarnings,
                "Legacy current view coverage is incomplete; targeted values do not prove whole-document coverage."];
            current.Complete = false;
        }
        else
        {
            current.Merge(walked, _maxBytes);
            var metadata = SemanticTargetedAuthority.ApplyCanonicalMetadata(document, current, _maxBytes, cancellationToken);
            if (!metadata.Applied)
            {
                current.Complete = false;
                current.OmittedCount = checked(current.OmittedCount
                    + Math.Max(1, metadata.IncompleteIds.Count));
                current.OmittedCountIsLowerBound = true;
                current.QueryWarnings = [.. current.QueryWarnings, metadata.Diagnostic!];
            }
        }
        foreach (var id in targetedCaptured.IncompleteIds) current.Fields.Remove(id);
        SemanticTargetedAuthority.Validate(original, current);
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
            && _captureFailures.Count == 0 && _captureOmittedCount == 0;
        IReadOnlyList<string> warnings = complete ? [] : ["Semantic review is incomplete: the original could not be opened, a view exceeded its memory or traversal limit, a lazy section could not be read, or an internal field could not be matched after a structural change. Only proven changes are listed."];
        return new SemanticChangeLog(changes, complete,
            [.. warnings, .. CaptureDiagnostics(), .. original.QueryWarnings.Concat(current.QueryWarnings).Distinct(StringComparer.Ordinal)], revision);
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

    private TargetedCaptureOutcome CaptureTargeted(ISaveDocument document, SemanticSnapshot snapshot,
        IReadOnlyList<string> valueIds, bool revokeOnFailure, CancellationToken cancellationToken) => SemanticTargetedReview.Capture(
            _targetedCapture, document, snapshot, valueIds, revokeOnFailure,
            RecordFailure, cancellationToken);

    private string[] CollectTargetIds(IEnumerable<string> valueIds, out int omittedCount)
    {
        var available = Math.Max(0, FieldMetadataLimits.MaxClosureIds - _targeted.Count);
        var ids = new List<string>(Math.Min(available, 64));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        omittedCount = 0;
        foreach (var id in valueIds)
        {
            if (string.IsNullOrEmpty(id) || _targeted.Contains(id) || seen.Contains(id)) continue;
            if (ids.Count < available)
            {
                seen.Add(id);
                ids.Add(id);
            }
            else
            {
                // Do not consume an untrusted or lazy affected-ID stream after the bound.
                // The diagnostic is intentionally a lower bound because the remainder is unknown.
                omittedCount = 1;
                break;
            }
        }
        return ids.ToArray();
    }

    private void RecordFailure(string id, string diagnostic)
    {
        if (_captureFailures.ContainsKey(id))
        {
            _captureFailures[id] = diagnostic;
            return;
        }

        if (_captureFailures.Count < FieldMetadataLimits.MaxClosureIds)
        {
            _captureFailures[id] = diagnostic;
            if (_failedOmissionIds.Add(id)) _captureOmittedCount++;
        }
        else
            RecordOmissions(1, "Per-ID targeted metadata diagnostics exceeded the bounded aggregate limit.");
    }

    private void RegisterOriginalRetryIds(IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            if (_originalRetryIds.Contains(id)) continue;
            if (_originalRetryIds.Count >= FieldMetadataLimits.MaxClosureIds)
            {
                RecordOmissions(1, "Original targeted metadata retry coverage exceeded its bounded limit.");
                continue;
            }

            _originalRetryIds.Add(id);
        }
    }

    private void RecordOriginalFailures(IReadOnlyList<string> ids, string diagnostic)
    {
        if (ids.Count == 0)
        {
            RecordOmissions(1, diagnostic);
            return;
        }

        RegisterOriginalRetryIds(ids);
        foreach (var id in ids)
            RecordFailure(id, diagnostic);
    }

    private void ClearRecoveredOriginalFailures(SemanticSnapshot snapshot)
    {
        foreach (var id in _originalRetryIds.ToArray())
        {
            if (!snapshot.Fields.TryGetValue(id, out var field) || !SemanticOriginalMetadata.HasMetadata(field)) continue;
            _originalRetryIds.Remove(id);
            if (_captureFailures.Remove(id) && _failedOmissionIds.Remove(id) && _captureOmittedCount > 0)
                _captureOmittedCount--;
        }
    }

    private void RecordOmissions(int count, string reason)
    {
        _captureOmittedCount = count > int.MaxValue - _captureOmittedCount
            ? int.MaxValue : _captureOmittedCount + count;
        _captureOmissionReason ??= reason;
    }

    private IReadOnlyList<string> CaptureDiagnostics()
    {
        if (_captureOmittedCount == 0) return _captureFailures.Values.ToArray();
        var aggregate = $"{_captureOmissionReason ?? "Targeted metadata coverage was bounded."} Omitted entries: {_captureOmittedCount}.";
        return [.. _captureFailures.Values, aggregate];
    }

}
