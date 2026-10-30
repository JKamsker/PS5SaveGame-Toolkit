using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.References;

namespace SaveToolkit.Core.Sessions;

internal sealed record SemanticField(string ValueId, string Label, string[] Path, FieldValue Value, bool ReadOnly,
    SemanticCollectionTarget? Target = null, string? CanonicalValueId = null,
    FieldBindingKind BindingKind = FieldBindingKind.Primary, FieldSpec? Spec = null,
    FieldPresentation? Presentation = null, bool MetadataPublished = false);
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
    private readonly SemanticReviewCoverageTracker _coverage = new();
    private readonly SemanticSnapshotCapture _capture;
    private readonly SemanticTargetedCapture _targetedCapture;
    private readonly Func<IReadOnlyList<string>, IReadOnlyDictionary<string, SemanticMemberContext>>? _memberContext;
    private readonly long _maxBytes;
    private readonly ReferenceFieldService _references;

    // Preflight can populate the baseline, but cancellation must leave it uncached.
    public Action CancellationRollback()
    {
        var original = _original?.Clone();
        var targeted = _targeted.ToArray();
        var targetedOnly = _targetedOnly;
        var metadataAvailable = _originalMetadataAvailable;
        var metadataAttempted = _originalMetadataAttempted;
        var coverage = _coverage.CaptureState();
        return () =>
        {
            _original = original?.Clone();
            _targeted.Clear();
            _targeted.UnionWith(targeted);
            _targetedOnly = targetedOnly;
            _originalMetadataAvailable = metadataAvailable;
            _originalMetadataAttempted = metadataAttempted;
            _coverage.Restore(coverage);
        };
    }
    public SemanticReview(DocumentSessionOptions limits, ReferenceFieldService? references = null,
        Func<IReadOnlyList<string>, IReadOnlyDictionary<string, SemanticMemberContext>>? memberContext = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxReviewQueryRows);
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxReviewQueryPages);
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxReviewQueryBytes);
        _capture = new SemanticSnapshotCapture(limits);
        _targetedCapture = new SemanticTargetedCapture(limits.MaxOriginalBytes);
        _memberContext = memberContext;
        _maxBytes = limits.MaxOriginalBytes;
        _references = references ?? new ReferenceFieldService(ReferenceContext.None);
    }
    internal SemanticReviewState CaptureState() => new(
        _original?.Clone(), _targeted.ToArray(), _coverage.Diagnostics());
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
                    _coverage.RecordOriginalFailures(metadata.IncompleteIds, metadata.Diagnostic!);
                }
                SemanticPresentationProjection.Apply(original, _original, _maxBytes, cancellationToken);
            }
            _originalMetadataAttempted = _coverage.RetryIds.Count == 0;
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
            _coverage.RecordOmissions(omittedCount, "The bounded targeted metadata aggregate limit was reached.");
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
                    _coverage.RegisterRetryIds(outcome.IncompleteIds);
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
                        _coverage.RecordOriginalFailures(metadata.IncompleteIds, metadata.Diagnostic!);
                    }
                    if (metadata.Applied) _coverage.ClearRecovered(snapshot);
                    SemanticPresentationProjection.Apply(original, snapshot, _maxBytes, cancellationToken);
                    _originalMetadataAvailable = true;
                    _originalMetadataAttempted = _coverage.RetryIds.Count == 0;
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
                if (_coverage.RetryIds.Count == 0)
                    _coverage.RecordOriginalFailures(ids, SemanticDiagnostics.Exception("Targeted metadata capture failed", ex));
                _original = new SemanticSnapshot(new Dictionary<string, SemanticField>(StringComparer.Ordinal), complete: false, bytes: 0)
                { QueryWarnings = [SemanticDiagnostics.Exception("Targeted metadata capture failed", ex)] };
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
            _coverage.RegisterRetryIds(outcome.IncompleteIds);
            _targeted.UnionWith(outcome.CapturedIds);
            var metadata = SemanticTargetedAuthority.ApplyCanonicalMetadata(original, existing, _maxBytes, cancellationToken);
            if (!metadata.Applied)
                _coverage.RecordOriginalFailures(metadata.IncompleteIds, metadata.Diagnostic!);
            else
                _coverage.ClearRecovered(existing);
            SemanticPresentationProjection.Apply(original, existing, _maxBytes, cancellationToken);
            _originalMetadataAttempted = _coverage.RetryIds.Count == 0;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _coverage.RecordOriginalFailures(ids, SemanticDiagnostics.Exception("Targeted metadata capture failed", ex));
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
        var hadBaseline = previous is not null;
        try
        {
            EnsureOriginal(openOriginal, cancellationToken);
            if (hadBaseline && (!_originalMetadataAttempted || _coverage.RetryIds.Count > 0
                    || _coverage.HasPendingMetadataAttempt)
                && _originalMetadataAvailable != false && _original is { } baseline)
            {
                var metadata = SemanticOriginalMetadata.Enrich(openOriginal, baseline, _maxBytes,
                    _coverage.RetryIds.ToArray(), _coverage.HasPendingMetadataAttempt,
                    ref _capturingOriginal, cancellationToken);
                var publication = PublishRecoveredTargets(metadata.Recovered);
                if (publication.Rejected.Count > 0)
                    _coverage.RecordOriginalFailures(publication.Rejected,
                        "Recovered original metadata could not be admitted to the bounded target set.");
                if (!metadata.Applied)
                    _coverage.RecordOriginalFailures(metadata.IncompleteIds, metadata.Diagnostic!);
                _coverage.ClearRecovered(baseline, publication.Accepted);
                if (metadata.Applied && publication.Rejected.Count == 0)
                {
                    _coverage.ClearMetadataAttemptFailure();
                }
                _originalMetadataAttempted = metadata.Applied && _coverage.RetryIds.Count == 0;
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
        var recoveryIds = _coverage.RetryIds.Where(id => !_targeted.Contains(id)).ToArray();
        var recoveryCaptured = CaptureTargeted(document, current, recoveryIds, revokeOnFailure: true, cancellationToken);
        foreach (var id in targetedCaptured.CapturedIds.Concat(recoveryCaptured.CapturedIds))
        {
            if (original.Fields.TryGetValue(id, out var before) && string.IsNullOrEmpty(before.CanonicalValueId))
                current.Fields.Remove(id);
        }
        var walked = _capture.Capture(document, index, expandedLazy, cancellationToken);
        if (_targetedOnly)
        {
            current.QueryWarnings = [.. current.QueryWarnings, .. walked.QueryWarnings,
                "Legacy current view coverage is incomplete; targeted values do not prove whole-document coverage."];
            current.Complete = false;
            SemanticPresentationProjection.Apply(document, current, _maxBytes, cancellationToken);
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
            SemanticPresentationProjection.Apply(document, current, _maxBytes, cancellationToken);
        }
        foreach (var id in targetedCaptured.IncompleteIds) current.Fields.Remove(id);
        SemanticChangedMetadataProjection.Apply(original, current, document, _maxBytes, cancellationToken);
        foreach (var id in recoveryCaptured.IncompleteIds) current.Fields.Remove(id);
        // A retry ID already contributes one original-coverage omission. Do not count the
        // same known identity a second time when the current target is still unavailable.
        if (recoveryCaptured.IncompleteIds.Count > 0)
        {
            current.OmittedCount = Math.Max(0, current.OmittedCount - recoveryCaptured.IncompleteIds.Count);
            if (current.OmittedCount == 0) current.OmittedCountIsLowerBound = false;
        }
        SemanticTargetedAuthority.Validate(original, current, _references);
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
        var noContext = new Dictionary<string, SemanticMemberContext>(StringComparer.Ordinal);
        var uncontextualized = SemanticSnapshotComparison.Compare(original.Fields, original.Complete,
            current.Fields, current.Complete, document, noContext, cancellationToken);
        var contextResolution = ResolveMemberContext(uncontextualized, Math.Max(original.Bytes, current.Bytes), cancellationToken);
        var changes = contextResolution.Contexts.Count == 0
            ? uncontextualized
            : SemanticSnapshotComparison.Compare(original.Fields, original.Complete,
                current.Fields, current.Complete, document, contextResolution.Contexts, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var complete = original.Complete && current.Complete && !_unreviewedReportedChanges
            && !_coverage.HasFailures && _coverage.OmittedCount == 0 && contextResolution.OmittedCount == 0;
        IReadOnlyList<string> warnings = complete ? [] : ["Semantic review is incomplete: the original could not be opened, a view exceeded its memory or traversal limit, a lazy section could not be read, or an internal field could not be matched after a structural change. Only proven changes are listed."];
        IReadOnlyList<string> allWarnings = [.. warnings, .. _coverage.Diagnostics(),
            .. (contextResolution.Diagnostic is { } diagnostic ? [diagnostic] : Array.Empty<string>()),
            .. original.QueryWarnings.Concat(current.QueryWarnings).Distinct(StringComparer.Ordinal)];
        var omitted = original.OmittedCount + current.OmittedCount + _coverage.OmittedCount + contextResolution.OmittedCount;
        var coverage = _originalMetadataAvailable == true || document is IFieldMetadataDocument || !complete
            ? new SemanticReviewCoverage(Math.Max(original.ExaminedCount, current.ExaminedCount), omitted, _maxBytes,
                Math.Max(original.Bytes, current.Bytes) + contextResolution.AcceptedBytes, allWarnings,
                original.OmittedCountIsLowerBound || current.OmittedCountIsLowerBound
                    || _coverage.OmittedCountIsLowerBound || contextResolution.OmittedCount > 0)
            : null;
        return new SemanticChangeLog(changes, complete, allWarnings, revision) { Coverage = coverage };
    }

    private MemberContextResolution ResolveMemberContext(IReadOnlyList<SemanticChange> changes, long usedBytes, CancellationToken cancellationToken) =>
        SemanticMemberContextResolver.Resolve(_memberContext, changes, usedBytes, _maxBytes, cancellationToken);
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
            _coverage.RecordFailure, cancellationToken);

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

    private RecoveryPublication PublishRecoveredTargets(IReadOnlyList<string> recoveredIds)
    {
        var pending = recoveredIds.Where(id => !_targeted.Contains(id))
            .Distinct(StringComparer.Ordinal).ToArray();
        var available = FieldMetadataLimits.MaxClosureIds - _targeted.Count;
        if (pending.Length > available)
            return new([], pending);

        foreach (var id in pending)
        {
            _targeted.Add(id);
        }
        return new(pending, []);
    }

}

internal sealed record RecoveryPublication(IReadOnlyList<string> Accepted, IReadOnlyList<string> Rejected);
