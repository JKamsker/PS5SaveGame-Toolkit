using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Sessions;

/// <summary>Owns bounded retry and diagnostic state for targeted semantic metadata.</summary>
internal sealed class SemanticReviewCoverage
{
    private readonly HashSet<string> _retryIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _failures = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failedOmissionIds = new(StringComparer.Ordinal);
    private int _omittedCount;
    private string? _omissionReason;
    private bool _pendingMetadataAttempt;

    public IReadOnlyCollection<string> RetryIds => _retryIds;
    public int OmittedCount => _omittedCount;
    public bool HasFailures => _failures.Count != 0;
    public bool HasPendingMetadataAttempt => _pendingMetadataAttempt;

    public SemanticReviewCoverageState CaptureState() => new(_retryIds.ToArray(), _failures.ToArray(),
        _failedOmissionIds.ToArray(), _omittedCount, _omissionReason, _pendingMetadataAttempt);

    public void Restore(SemanticReviewCoverageState state)
    {
        _retryIds.Clear();
        _retryIds.UnionWith(state.RetryIds);
        _failures.Clear();
        foreach (var (id, diagnostic) in state.Failures) _failures[id] = diagnostic;
        _failedOmissionIds.Clear();
        _failedOmissionIds.UnionWith(state.FailedOmissionIds);
        _omittedCount = state.OmittedCount;
        _omissionReason = state.OmissionReason;
        _pendingMetadataAttempt = state.PendingMetadataAttempt;
    }

    public void RecordFailure(string id, string diagnostic)
    {
        if (_failures.ContainsKey(id))
        {
            _failures[id] = SemanticDiagnostics.Bound(diagnostic);
            return;
        }

        if (_failures.Count < FieldMetadataLimits.MaxClosureIds)
        {
            _failures[id] = SemanticDiagnostics.Bound(diagnostic);
            if (_failedOmissionIds.Add(id)) _omittedCount++;
        }
        else
            RecordOmissions(1, "Per-ID targeted metadata diagnostics exceeded the bounded aggregate limit.");
    }

    public void RegisterRetryIds(IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            if (_retryIds.Contains(id)) continue;
            if (_retryIds.Count >= FieldMetadataLimits.MaxClosureIds)
            {
                RecordOmissions(1, "Original targeted metadata retry coverage exceeded its bounded limit.");
                continue;
            }

            _retryIds.Add(id);
        }
    }

    public void RecordOriginalFailures(IReadOnlyList<string> ids, string diagnostic)
    {
        if (ids.Count == 0)
        {
            RecordMetadataAttemptFailure(diagnostic);
            return;
        }

        RegisterRetryIds(ids);
        foreach (var id in ids) RecordFailure(id, diagnostic);
    }

    public void ClearRecovered(SemanticSnapshot snapshot, IReadOnlyList<string>? recoveredIds = null)
    {
        var ids = recoveredIds ?? _retryIds.ToArray();
        foreach (var id in ids)
        {
            if (!_retryIds.Contains(id)) continue;
            if (!snapshot.Fields.TryGetValue(id, out var field) || !SemanticOriginalMetadata.HasMetadata(field)) continue;
            _retryIds.Remove(id);
            if (_failures.Remove(id) && _failedOmissionIds.Remove(id) && _omittedCount > 0)
                _omittedCount--;
        }
    }

    public void ClearMetadataAttemptFailure()
    {
        if (!_pendingMetadataAttempt) return;
        _pendingMetadataAttempt = false;
        if (_omittedCount > 0) _omittedCount--;
    }

    private void RecordMetadataAttemptFailure(string diagnostic)
    {
        if (_pendingMetadataAttempt) return;
        _pendingMetadataAttempt = true;
        RecordOmissions(1, diagnostic);
    }

    public void RecordOmissions(int count, string reason)
    {
        _omittedCount = count > int.MaxValue - _omittedCount ? int.MaxValue : _omittedCount + count;
        _omissionReason ??= SemanticDiagnostics.Bound(reason);
    }

    public IReadOnlyList<string> Diagnostics()
    {
        if (_omittedCount == 0) return _failures.Values.ToArray();
        var aggregate = $"{_omissionReason ?? "Targeted metadata coverage was bounded."} Omitted entries: {_omittedCount}.";
        return [.. _failures.Values, aggregate];
    }
}

internal sealed record SemanticReviewCoverageState(IReadOnlyList<string> RetryIds,
    IReadOnlyList<KeyValuePair<string, string>> Failures, IReadOnlyList<string> FailedOmissionIds,
    int OmittedCount, string? OmissionReason, bool PendingMetadataAttempt);
