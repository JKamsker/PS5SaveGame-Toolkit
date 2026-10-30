using System.Text.Json;
using SaveToolkit.Core.Presets;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Sessions;

/// <summary>Revision-bound preset preview. Resolution is included so callers can explain skipped targets.</summary>
public sealed record PresetActionPreview(
    string? PlanId,
    long Revision,
    ToolkitPreset Preset,
    PresetResolution Resolution,
    ActionPlan Plan)
{
    public bool Stale { get; init; }
    public string? Code => Stale ? "stale" : null;
}

public sealed partial class DocumentSession
{
    private PendingPreset? _pendingPreset;

    /// <summary>Runs the optional pure plugin capture adapter while the document is locked.</summary>
    public PresetAction CapturePreset(PresetCaptureRequest request) => Locked(() =>
    {
        ArgumentNullException.ThrowIfNull(request);
        var effectiveRequest = request with
        {
            SelectedRowKeys = request.ScopeKind == PresetScopeKinds.Selected ? request.SelectedRowKeys : null,
        };
        var capability = PresetCapability;
        if (Document is IPresetDocument && capability is null)
            throw new ArgumentException("The semantic preset capability is malformed.", nameof(request));
        if (PresetContractValidation.ValidateCapture(capability, effectiveRequest, _referenceFields) is { } error)
            throw new ArgumentException(error, nameof(request));
        var action = Document is IPresetDocument adapter
            ? adapter.CapturePreset(effectiveRequest)
            : new PresetAction(effectiveRequest.ActionId, effectiveRequest.Parameters, [], effectiveRequest.ScopeKind);
        if (PresetContractValidation.ValidateCapturedAction(capability, effectiveRequest, action, _referenceFields) is { } actionError)
            throw new ArgumentException(actionError, nameof(request));
        return action;
    });

    public PresetActionPreview PlanPreset(ToolkitPreset preset, long? expectedRevision = null,
        IReadOnlyDictionary<string, string>? choices = null, CancellationToken cancellationToken = default) => Locked(() => RunPreflight(_ => PlanPresetCore(preset, expectedRevision, choices, cancellationToken), cancellationToken), cancellationToken);

    public SessionEditResult ApplyPresetPlan(string planId, long expectedRevision, CancellationToken cancellationToken = default) =>
        ApplyPresetPlan(planId, expectedRevision, null, null, cancellationToken);

    public SessionEditResult ApplyPresetPlan(string planId, long expectedRevision, string? expectedPresetId,
        string? expectedPresetFingerprint, CancellationToken cancellationToken = default) =>
        Locked(() => RunPreflight(startApply => ApplyPresetCore(planId, expectedRevision, startApply, expectedPresetId, expectedPresetFingerprint, cancellationToken), cancellationToken), cancellationToken);

    private PresetActionPreview PlanPresetCore(ToolkitPreset preset, long? expectedRevision, IReadOnlyDictionary<string, string>? choices, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preset);
        _pendingPreset = null;
        var emptyResolution = new PresetResolution([]);
        if (StaleCheck(expectedRevision, [preset.Id]) is { } stale)
            return new(null, _revision, preset, emptyResolution, new ActionPlan { Errors = stale.Errors }) { Stale = true };
        if (Document is not IPresetDocument planner || PresetCapability is not { } capability)
            return Error(preset, emptyResolution, "This plugin has no semantic preset adapter.");
        if (PresetContractValidation.ValidateChoices(choices) is { } choiceError)
            return Error(preset, emptyResolution, choiceError);
        if (PresetContractValidation.ValidateActions(capability, preset.Actions, _referenceFields) is { } actionError)
            return Error(preset, emptyResolution, actionError);
        if (preset.Actions.Count == 0 || preset.Actions.Count > capability.MaxActions)
            return Error(preset, emptyResolution, "The preset contains an unsupported number of actions.");
        var selected = choices is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(choices, StringComparer.Ordinal);
        var resolution = emptyResolution;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var instances = preset.Actions.SelectMany(action => action.Instances ?? []).ToArray();
            var candidateResolution = planner.ResolvePresetInstances(instances, new Dictionary<string, string>(StringComparer.Ordinal));
            if (PresetContractValidation.ValidateResolution(candidateResolution, instances, null, false, out _) is { } resolutionError)
                return Error(preset, emptyResolution, resolutionError);
            if (PresetContractValidation.DetachResolution(candidateResolution, out var initialResolution, out var resolutionBytes) is { } detachError)
                return Error(preset, emptyResolution, detachError);
            resolution = initialResolution!;
            if (selected.Count > 0)
            {
                if (PresetContractValidation.ValidateResolution(resolution, instances, selected, false, out _) is { } resolutionChoiceError)
                    return Error(preset, resolution, resolutionChoiceError);
                candidateResolution = planner.ResolvePresetInstances(instances, selected);
                if (PresetContractValidation.ValidateResolution(candidateResolution, instances, selected, true, out _) is { } chosenError)
                    return Error(preset, resolution, chosenError);
                if (PresetContractValidation.DetachResolution(candidateResolution, out var chosenResolution, out resolutionBytes) is { } chosenDetachError)
                    return Error(preset, resolution, chosenDetachError);
                resolution = chosenResolution!;
            }
            if (!resolution.Succeeded)
                return new(null, _revision, preset, resolution, new ActionPlan { Errors = ResolutionErrors(resolution) });
            var plan = planner.PlanPreset(preset.Actions, CloneResolution(resolution));
            var bytes = JsonSerializer.SerializeToUtf8Bytes(plan);
            if (bytes.LongLength > MaxPlanBytes)
                return Error(preset, resolution, "Preset preview is too large; narrow its scope.");
            if (plan.Errors.Count > 0) return new(null, _revision, preset, resolution, plan);
            var id = Guid.NewGuid().ToString("N");
            _pendingPreset = new PendingPreset(id, _revision, preset, PresetStore.PayloadFingerprint(preset), resolution, bytes, resolutionBytes!, selected);
            return new(id, _revision, preset, resolution, plan);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            return Error(preset, resolution, $"Preset planning failed: {ex.Message}");
        }

        PresetActionPreview Error(ToolkitPreset item, PresetResolution resolutionResult, string message) =>
            new(null, _revision, item, resolutionResult, new ActionPlan { Errors = [new("preset", message)] });
    }

    private SessionEditResult ApplyPresetCore(string planId, long expectedRevision, Action startApply,
        string? expectedPresetId, string? expectedPresetFingerprint, CancellationToken cancellationToken)
    {
        var pending = _pendingPreset;
        _pendingPreset = null;
        if (pending is null || pending.Id != planId || pending.Revision != expectedRevision || expectedRevision != _revision
            || expectedPresetId is not null && (pending.Preset.Id != expectedPresetId || pending.PresetFingerprint != expectedPresetFingerprint))
            return Rejected(string.Empty, "The preset preview is stale. Preview it again.") with { Stale = true };
        if (!Document.CanEdit) return Rejected("preset", "This document is read-only.");
        if (Document is not IPresetDocument planner || PresetCapability is not { } capability)
            return Rejected("preset", "This plugin has no semantic preset adapter.");
        try
        {
            if (PresetContractValidation.ValidateActions(capability, pending.Preset.Actions, _referenceFields) is { } actionError)
                return Rejected("preset", actionError);
            cancellationToken.ThrowIfCancellationRequested();
            var instances = pending.Preset.Actions.SelectMany(action => action.Instances ?? []).ToArray();
            var candidateResolution = planner.ResolvePresetInstances(instances, new Dictionary<string, string>(StringComparer.Ordinal));
            if (PresetContractValidation.ValidateResolution(candidateResolution, instances, null, false, out _) is { } resolutionError)
                return Rejected("preset", resolutionError) with { Stale = true };
            if (PresetContractValidation.DetachResolution(candidateResolution, out var detachedResolution, out var freshResolutionBytes) is { } detachError)
                return Rejected("preset", detachError) with { Stale = true };
            var resolution = detachedResolution!;
            if (pending.Choices.Count > 0)
            {
                if (PresetContractValidation.ValidateResolution(resolution, instances, pending.Choices, false, out _) is { } choiceError)
                    return Rejected("preset", choiceError) with { Stale = true };
                candidateResolution = planner.ResolvePresetInstances(instances, pending.Choices);
                if (PresetContractValidation.ValidateResolution(candidateResolution, instances, pending.Choices, true, out _) is { } chosenError)
                    return Rejected("preset", chosenError) with { Stale = true };
                if (PresetContractValidation.DetachResolution(candidateResolution, out var chosenResolution, out freshResolutionBytes) is { } chosenDetachError)
                    return Rejected("preset", chosenDetachError) with { Stale = true };
                resolution = chosenResolution!;
            }
            if (!resolution.Succeeded) return new SessionEditResult { Errors = ResolutionErrors(resolution), State = StateUnlocked() };
            var fresh = planner.PlanPreset(pending.Preset.Actions, CloneResolution(resolution));
            if (fresh.Errors.Count > 0 || !pending.PlanBytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(fresh))
                || !pending.ResolutionBytes.AsSpan().SequenceEqual(freshResolutionBytes!))
                return Rejected("preset", "The preset effects changed. Preview it again.") with { Stale = true };
            cancellationToken.ThrowIfCancellationRequested();
            startApply();
            return RunGroup(_ =>
            {
                var outcome = planner.ApplyPreset(pending.Preset.Actions, CloneResolution(resolution), fresh);
                if (!outcome.Succeeded) return FromOutcome(outcome);
                var warnings = outcome.Revert is { } revert ? Record(new MementoStep(revert))
                    : outcome.Changes.Count > 0 ? Record(new ValueStep(outcome.Changes)) : [];
                return AfterChange(FromOutcome(outcome), outcome.StructureChanged || outcome.Revert is not null, warnings);
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            return Rejected("preset", $"Preset apply failed: {ex.Message}") with { State = StateUnlocked() };
        }
    }

    private static IReadOnlyList<FieldError> ResolutionErrors(PresetResolution resolution) =>
        resolution.Instances.Where(item => item.Status != PresetInstanceResolutionStatus.Resolved)
            .Select(item => new FieldError(item.Requested.StableId, item.Message ?? $"Preset instance is {item.Status.ToString().ToLowerInvariant()}.")).ToArray();

    private static PresetResolution CloneResolution(PresetResolution source) => new(source.Instances.Select(item =>
        new PresetInstanceResolution(item.Requested with { }, item.Status,
            (item.Candidates ?? []).Select(candidate => candidate with { }).ToArray(), item.Message)).ToArray());

    private sealed record PendingPreset(string Id, long Revision, ToolkitPreset Preset, string PresetFingerprint, PresetResolution Resolution,
        byte[] PlanBytes, byte[] ResolutionBytes, IReadOnlyDictionary<string, string> Choices);
}
