using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Core.Editing;

namespace SaveToolkit.Core.Sessions.Groups;

/// <summary>
/// Runs mutations and restores on independent candidates. The owning DocumentSession serializes calls
/// and supplies F5 history/F6 plans. A failed plugin call never reaches the published group model.
/// </summary>
internal sealed class StagedGroupDocument : ISaveDocument, IFieldMetadataDocument, IActionEffectDocument,
    IQueryableDocument, IAdditionalOutputOwner
{
    private const int MaxRememberedQueries = 256;
    private readonly Func<IGroupedSaveDocument> _open;
    private readonly Func<IGroupedSaveDocument, IReadOnlyList<FileOutput>> _verify;
    private readonly Action<CancellationToken> _preflight;
    private readonly long _maxSnapshotBytes;
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private readonly HashSet<string> _resolvedMetadataIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Collection, QueryableCollectionQuery Query)> _queries = new(StringComparer.Ordinal);
    private IGroupedSaveDocument _current;
    private IReadOnlyList<FileOutput> _outputs;

    public StagedGroupDocument(Func<IGroupedSaveDocument> open,
        Func<IGroupedSaveDocument, IReadOnlyList<FileOutput>> verify, Action preflight, long maxSnapshotBytes)
        : this(open, verify, _ => preflight(), maxSnapshotBytes) { }

    public StagedGroupDocument(Func<IGroupedSaveDocument> open,
        Func<IGroupedSaveDocument, IReadOnlyList<FileOutput>> verify, Action<CancellationToken> preflight, long maxSnapshotBytes)
    {
        _open = open;
        _verify = verify;
        _preflight = preflight;
        _maxSnapshotBytes = maxSnapshotBytes;
        _current = open();
        try { _outputs = verify(_current); }
        catch (Exception failure)
        {
            SessionCleanup.DisposeAfterFailure(_current, failure, "Initial group verification and cleanup failed.");
            throw;
        }
    }

    public string Title => _current.Title;
    public bool CanEdit => _current.CanEdit;
    public IReadOnlyList<string> AdditionalOutputPaths =>
        _current is IAdditionalOutputOwner owner ? owner.AdditionalOutputPaths : [];
    public ViewNode BuildView() => _current.BuildView();
    public FieldValue GetValue(string valueId) => _current.GetValue(valueId);

    internal bool MetadataEnabled => _current is IFieldMetadataDocument;
    internal bool EffectEnabled => _current is IActionEffectDocument;

    public DocumentMemento CaptureMemento() => _current is IActionEffectDocument effects
        ? effects.CaptureMemento()
        : throw new NotSupportedException("The current group provider does not support action effects.");

    /// <summary>Forward bounded metadata to the currently published candidate.</summary>
    public IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
    {
        if (_current is not IFieldMetadataDocument metadata) return [];
        var resolved = metadata.ResolveFieldMetadata(valueIds);
        foreach (var field in resolved)
        {
            if (_resolvedMetadataIds.Count >= FieldMetadataLimits.MaxClosureIds) break;
            _resolvedMetadataIds.Add(field.ValueId);
        }

        return resolved;
    }
    public AssetContent? GetAsset(string assetId) => _current.GetAsset(assetId);
    public ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count) => _current.ReadBlob(blobId, offset, count);

    public IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        var nodes = _current.ExpandNode(lazyId);
        _expanded.Add(lazyId);
        return nodes;
    }

    public IReadOnlyList<QueryableCollection> Collections =>
        _current is IQueryableDocument queryable ? queryable.Collections : [];

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
    {
        var copy = query with { Filters = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>(query.Filters)) };
        var key = collectionId + "\n" + System.Text.Json.JsonSerializer.Serialize(copy);
        if (!_queries.ContainsKey(key) && _queries.Count >= MaxRememberedQueries)
            throw new InvalidOperationException("This group has reached its 256 retained query pages. Close and reopen it to release page bindings.");
        var page = Queryable(_current).Query(collectionId, copy);
        _queries[key] = (collectionId, copy);
        return page;
    }

    public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) =>
        Mutate(candidate => candidate.ApplyEdits(edits), edits.Select(edit => edit.ValueId).ToArray());

    public ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        string? message = null;
        var outcome = Mutate(candidate =>
        {
            var action = candidate.InvokeAction(actionId, parameters);
            message = action.Message;
            return new EditOutcome
            {
                Changes = action.Changes, Warnings = action.Warnings,
                StructureChanged = action.StructureChanged || !action.Undoable, Revert = action.Revert,
            };
        });
        if (!outcome.Succeeded) throw new SaveFormatException(string.Join("; ", outcome.Errors.Select(e => e.Message)));
        return new ActionOutcome
        {
            Changes = outcome.Changes, Warnings = outcome.Warnings, Message = message,
            StructureChanged = outcome.StructureChanged, Revert = outcome.Revert,
        };
    }

    // Even a buggy planner cannot mutate the live model. The usual F6 deterministic revalidation
    // still runs, with the same identities, pinned context and group-wide revision.
    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
        => PlanForSession(actionId, parameters, scope, CancellationToken.None);

    internal ActionPlan PlanForSession(string actionId, IReadOnlyDictionary<string, FieldValue> parameters,
        ActionScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var memento = _current.CaptureMemento();
        cancellationToken.ThrowIfCancellationRequested();
        var candidate = CreateCandidate(memento, out _, rebind: true, includeResolvedMetadata: true,
            cancellationToken: cancellationToken);
        try
        {
            var plan = Planner(candidate).PlanAction(actionId, parameters, scope);
            cancellationToken.ThrowIfCancellationRequested();
            var completed = candidate;
            candidate = null!;
            completed.Dispose();
            return plan;
        }
        catch (Exception failure)
        {
            SessionCleanup.DisposeAfterFailure(candidate, failure, "Group action planning and cleanup failed.");
            throw;
        }
    }

    internal Action CancellationRollback()
    {
        var expanded = _expanded.ToArray();
        var queries = _queries.ToArray();
        var metadataIds = _resolvedMetadataIds.ToArray();
        return () =>
        {
            _expanded.Clear();
            _expanded.UnionWith(expanded);
            _queries.Clear();
            foreach (var query in queries) _queries.Add(query.Key, query.Value);
            _resolvedMetadataIds.Clear();
            _resolvedMetadataIds.UnionWith(metadataIds);
        };
    }

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan) =>
        Mutate(candidate => Planner(candidate).ApplyActionPlan(actionId, parameters, scope, plan), plan);

    public IReadOnlyList<FileOutput> Serialize() => SerializeForSnapshot(CancellationToken.None)
        .Select(output => output with { Content = output.Content.ToArray() }).ToArray();

    // Only the session snapshotter consumes this internal view; it copies the buffers under
    // the session gate before returning them to any caller.
    internal IReadOnlyList<FileOutput> SerializeForSnapshot(CancellationToken cancellationToken)
    {
        _preflight(cancellationToken);
        return _outputs;
    }

    public DocumentMemento RestoreMemento(DocumentMemento memento)
    {
        var inverse = _current.CaptureMemento();
        var candidate = CreateCandidate(memento, out var retiredMetadataIds, rebind: false,
            includeResolvedMetadata: true);
        IReadOnlyList<FileOutput> outputs;
        try { outputs = _verify(candidate); }
        catch (Exception failure)
        {
            SessionCleanup.DisposeAfterFailure(candidate, failure, "Group restore verification and cleanup failed.");
            throw;
        }
        Publish(candidate, outputs);
        RetireMetadataIds(retiredMetadataIds);
        RetireMetadataIds(ResolveCandidateMetadata(candidate, includeResolvedMetadata: true, additionalIds: null,
            CancellationToken.None));
        return inverse;
    }

    public void Dispose() => _current.Dispose();

    private EditOutcome Mutate(Func<IGroupedSaveDocument, EditOutcome> mutation, ActionPlan? plan = null) =>
        MutateCore(mutation, plan?.Changes.Where(change => change.Effect is null)
            .Select(change => change.ValueId).ToArray(), plan);

    private EditOutcome Mutate(Func<IGroupedSaveDocument, EditOutcome> mutation, IReadOnlyList<string> metadataIds) =>
        MutateCore(mutation, metadataIds, null);

    private EditOutcome MutateCore(Func<IGroupedSaveDocument, EditOutcome> mutation,
        IReadOnlyList<string>? metadataIds, ActionPlan? plan)
    {
        var before = _current.CaptureMemento();
        var candidate = CreateCandidate(before, out var retiredMetadataIds,
            includeResolvedMetadata: metadataIds is not null, metadataIds: metadataIds);
        try
        {
            var outcome = mutation(candidate);
            if (outcome.Succeeded && plan is { Changes.Count: > 0 } && plan.Changes.Any(change => change.Effect is not null))
                ActionEffectOutcomeValidation.Validate(plan, outcome);
            if (!outcome.Succeeded || outcome.Changes.Count == 0 && !outcome.StructureChanged && outcome.Revert is null)
            {
                var discarded = candidate;
                candidate = null;
                return DisposeDiscarded(discarded, outcome);
            }
            CheckSnapshotBudget(before);
            var outputs = _verify(candidate);
            // A group promises both undo and redo. Refuse the candidate before publication if
            // either whole-group token would trigger F5's single-step non-undoable fallback.
            CheckSnapshotBudget(candidate.CaptureMemento());
            // Check the new view before publication as well. Every committed group mutation is a
            // structural boundary, so all F8 pages/lazy views and F6 plans become stale together.
            _ = candidate.BuildView();
            var postMutationRetired = ResolveCandidateMetadata(candidate, includeResolvedMetadata: true,
                additionalIds: null, CancellationToken.None);
            Publish(candidate, outputs);
            RetireMetadataIds(retiredMetadataIds);
            RetireMetadataIds(postMutationRetired);
            TrackMetadataIds(metadataIds);
            candidate = null;
            return outcome with { Revert = before, StructureChanged = true };
        }
        catch (Exception failure)
        {
            if (candidate is not null)
                SessionCleanup.DisposeAfterFailure(candidate, failure, "Group mutation verification and cleanup failed.");
            throw;
        }
    }

    private void CheckSnapshotBudget(DocumentMemento memento)
    {
        if (memento.SizeBytes < 0 || memento.SizeBytes > _maxSnapshotBytes)
            throw new SaveFormatException("The group snapshot exceeds its undo budget. No changes were published.");
    }

    private static EditOutcome DisposeDiscarded(IGroupedSaveDocument candidate, EditOutcome outcome)
    {
        // EditOutcome is the only structured rejection channel; ActionOutcome has no error list.
        // A discarded candidate therefore keeps the plugin errors and appends cleanup failure.
        try
        {
            candidate.Dispose();
            return outcome;
        }
        catch (Exception cleanup) when (cleanup is not OutOfMemoryException)
        {
            var error = new FieldError(string.Empty, $"Group candidate cleanup failed: {cleanup.Message}");
            return outcome with { Errors = [.. outcome.Errors, error] };
        }
    }

    private IGroupedSaveDocument CreateCandidate(DocumentMemento memento,
        out IReadOnlyList<string> retiredMetadataIds, bool rebind = true,
        bool includeResolvedMetadata = false, IReadOnlyList<string>? metadataIds = null,
        CancellationToken cancellationToken = default)
    {
        _preflight(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var candidate = _open();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = candidate.RestoreMemento(memento);
            cancellationToken.ThrowIfCancellationRequested();
            _ = candidate.BuildView();
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var lazyId in rebind ? _expanded : [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { _ = candidate.ExpandNode(lazyId); }
                catch (KeyNotFoundException) { } // An earlier structural commit retired this section.
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (rebind && candidate is IQueryableDocument queryable)
                foreach (var (collection, query) in _queries.Values)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (queryable.Collections.Any(c => c.Id == collection)) _ = queryable.Query(collection, query);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            retiredMetadataIds = ResolveCandidateMetadata(candidate, includeResolvedMetadata, metadataIds, cancellationToken);
            return candidate;
        }
        catch (Exception failure)
        {
            SessionCleanup.DisposeAfterFailure(candidate, failure, "Group candidate construction and cleanup failed.");
            throw;
        }
    }

    private IReadOnlyList<string> ResolveCandidateMetadata(IGroupedSaveDocument candidate, bool includeResolvedMetadata,
        IReadOnlyList<string>? additionalIds, CancellationToken cancellationToken)
    {
        if (candidate is not IFieldMetadataDocument) return [];
        var ids = (includeResolvedMetadata ? _resolvedMetadataIds : [])
            .Concat(additionalIds ?? [])
            .Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return [];
        if (ids.Length > FieldMetadataLimits.MaxClosureIds)
            throw new InvalidOperationException($"Group candidate metadata exceeds the bounded limit of {FieldMetadataLimits.MaxClosureIds} IDs.");

        var required = (additionalIds ?? []).Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.Ordinal);
        var index = new FieldMetadataIndex(candidate);
        index.ResolveAll(ids, FieldMetadataLimits.MaxClosureIds, cancellationToken);
        var retired = ids.Where(id => index.Cached(id) is null && !required.Contains(id)).ToArray();
        if (ids.Any(id => index.Cached(id) is null && required.Contains(id)))
            throw new InvalidOperationException("Group candidate metadata could not rebind every affected field.");
        return retired;
    }

    private void RetireMetadataIds(IReadOnlyList<string> ids)
    {
        foreach (var id in ids) _resolvedMetadataIds.Remove(id);
    }

    private void TrackMetadataIds(IReadOnlyList<string>? ids)
    {
        if (ids is null) return;
        foreach (var id in ids)
        {
            if (_resolvedMetadataIds.Count >= FieldMetadataLimits.MaxClosureIds) break;
            if (!string.IsNullOrEmpty(id)) _resolvedMetadataIds.Add(id);
        }
    }

    private void Publish(IGroupedSaveDocument candidate, IReadOnlyList<FileOutput> outputs)
    {
        var previous = _current;
        _current = candidate;
        _outputs = outputs;
        _queries.Clear();
        // Disposal is cleanup after publication, not a transaction failure.
        try { previous.Dispose(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
    }

    private static IActionPlanningDocument Planner(IGroupedSaveDocument document) =>
        document as IActionPlanningDocument ?? throw new NotSupportedException("This group does not support action planning.");

    private static IQueryableDocument Queryable(IGroupedSaveDocument document) =>
        document as IQueryableDocument ?? throw new KeyNotFoundException("This group has no queryable collections.");
}
