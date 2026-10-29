using System.Text.Json;
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
        Document is IPresetDocument adapter ? adapter.CapturePreset(request) :
        new PresetAction(request.ActionId, request.Parameters, [], request.ScopeKind));

    public PresetActionPreview PlanPreset(ToolkitPreset preset, long? expectedRevision = null,
        IReadOnlyDictionary<string, string>? choices = null, CancellationToken cancellationToken = default) => Locked(() => RunPreflight(_ => PlanPresetCore(preset, expectedRevision, choices, cancellationToken), cancellationToken), cancellationToken);

    public SessionEditResult ApplyPresetPlan(string planId, long expectedRevision, CancellationToken cancellationToken = default) =>
        Locked(() => RunPreflight(startApply => ApplyPresetCore(planId, expectedRevision, startApply, cancellationToken), cancellationToken), cancellationToken);

    private PresetActionPreview PlanPresetCore(ToolkitPreset preset, long? expectedRevision, IReadOnlyDictionary<string, string>? choices, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preset);
        _pendingPreset = null;
        var emptyResolution = new PresetResolution([]);
        if (StaleCheck(expectedRevision, [preset.Id]) is { } stale)
            return new(null, _revision, preset, emptyResolution, new ActionPlan { Errors = stale.Errors }) { Stale = true };
        if (Document is not IPresetDocument planner)
            return Error(preset, emptyResolution, "This plugin has no semantic preset adapter.");
        if (preset.Actions.Count == 0 || preset.Actions.Count > planner.PresetCapability.MaxActions)
            return Error(preset, emptyResolution, "The preset contains an unsupported number of actions.");
        var instances = preset.Actions.SelectMany(action => action.Instances ?? []).ToArray();
        var selected = choices is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(choices, StringComparer.Ordinal);
        var resolution = planner.ResolvePresetInstances(instances, selected);
        if (!resolution.Succeeded)
            return new(null, _revision, preset, resolution, new ActionPlan { Errors = ResolutionErrors(resolution) });
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plan = planner.PlanPreset(preset.Actions, resolution);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(plan);
            if (bytes.LongLength > MaxPlanBytes)
                return Error(preset, resolution, "Preset preview is too large; narrow its scope.");
            if (plan.Errors.Count > 0) return new(null, _revision, preset, resolution, plan);
            var id = Guid.NewGuid().ToString("N");
            var resolutionBytes = JsonSerializer.SerializeToUtf8Bytes(resolution);
            _pendingPreset = new PendingPreset(id, _revision, preset, resolution, bytes, resolutionBytes, selected);
            return new(id, _revision, preset, resolution, plan);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            return Error(preset, resolution, $"Preset planning failed: {ex.Message}");
        }

        PresetActionPreview Error(ToolkitPreset item, PresetResolution resolutionResult, string message) =>
            new(null, _revision, item, resolutionResult, new ActionPlan { Errors = [new("preset", message)] });
    }

    private SessionEditResult ApplyPresetCore(string planId, long expectedRevision, Action startApply, CancellationToken cancellationToken)
    {
        var pending = _pendingPreset;
        _pendingPreset = null;
        if (pending is null || pending.Id != planId || pending.Revision != expectedRevision || expectedRevision != _revision)
            return Rejected(string.Empty, "The preset preview is stale. Preview it again.") with { Stale = true };
        if (!Document.CanEdit) return Rejected("preset", "This document is read-only.");
        if (Document is not IPresetDocument planner) return Rejected("preset", "This plugin has no semantic preset adapter.");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolution = planner.ResolvePresetInstances(pending.Preset.Actions.SelectMany(action => action.Instances ?? []).ToArray(), pending.Choices);
            if (!resolution.Succeeded) return new SessionEditResult { Errors = ResolutionErrors(resolution), State = StateUnlocked() };
            var fresh = planner.PlanPreset(pending.Preset.Actions, resolution);
            if (fresh.Errors.Count > 0 || !pending.PlanBytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(fresh))
                || !pending.ResolutionBytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(resolution)))
                return Rejected("preset", "The preset effects changed. Preview it again.") with { Stale = true };
            cancellationToken.ThrowIfCancellationRequested();
            startApply();
            return RunGroup(_ =>
            {
                var outcome = planner.ApplyPreset(pending.Preset.Actions, resolution, fresh);
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

    private sealed record PendingPreset(string Id, long Revision, ToolkitPreset Preset, PresetResolution Resolution,
        byte[] PlanBytes, byte[] ResolutionBytes, IReadOnlyDictionary<string, string> Choices);
}
