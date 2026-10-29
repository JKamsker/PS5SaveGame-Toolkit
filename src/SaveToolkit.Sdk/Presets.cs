using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaveToolkit.Sdk;

/// <summary>Stable title, plugin and optional game-build identity used by an external toolkit preset.</summary>
public sealed record PresetCompatibility(
    string PluginId,
    string PluginVersion,
    IReadOnlyList<string> TitleIds,
    string? GameBuild = null,
    string? FormatVariant = null,
    string? ReferencePackIdentity = null,
    bool RequiresExplicitInstanceResolution = true,
    string PresetKind = "toolkit.action-parameters",
    string? SchemaFingerprint = null,
    string? FileRole = null,
    string? CapabilitySchemaVersion = null);

/// <summary>A semantic instance reference. Stable ids are supplied by a plugin; session ids are forbidden.</summary>
public sealed record PresetInstanceReference(
    string Kind,
    string StableId,
    string? Label = null,
    string? Role = null,
    string? Path = null,
    string? Build = null,
    string? Provenance = null);

/// <summary>One action configuration persisted by a toolkit preset.</summary>
public sealed record PresetAction(
    string ActionId,
    IReadOnlyDictionary<string, JsonElement> Parameters,
    IReadOnlyList<PresetInstanceReference> Instances = null!,
    string ScopeKind = PresetScopeKinds.Document);

/// <summary>Ephemeral capture input. Selected row keys are resolved immediately and never persisted.</summary>
public sealed record PresetCaptureRequest(
    string ActionId,
    IReadOnlyDictionary<string, JsonElement> Parameters,
    string ScopeKind = PresetScopeKinds.Document,
    IReadOnlyList<string>? SelectedRowKeys = null,
    bool AllMatches = false);

/// <summary>Stable semantic scope names; none of these values is a session row key.</summary>
public static class PresetScopeKinds
{
    /// <summary>Apply to the current document's eligible rows.</summary>
    public const string Document = "document";
    /// <summary>Apply to all currently eligible rows selected by the plugin.</summary>
    public const string AllEligible = "allEligible";
    /// <summary>Apply to all owned copies of a semantic type.</summary>
    public const string AllCopies = "allCopies";
    /// <summary>Apply only to explicitly resolved semantic targets.</summary>
    public const string Selected = "selected";
}

/// <summary>Versioned, host-owned preset payload. It is never embedded in save bytes.</summary>
public sealed record ToolkitPreset(
    string SchemaVersion,
    string Id,
    string Name,
    PresetCompatibility Compatibility,
    IReadOnlyList<PresetAction> Actions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Published action metadata a plugin is willing to capture or apply through presets.</summary>
public sealed record PresetActionCapability(
    string ActionId,
    string Label,
    IReadOnlyList<string> ParameterIds,
    bool SupportsCapture = true,
    bool SupportsApply = true,
    IReadOnlyList<Values.FieldSpec> ParameterSpecs = null!,
    string ScopeKind = PresetScopeKinds.Document,
    IReadOnlyList<string> ScopeKinds = null!)
{
    /// <summary>All semantic scopes advertised by this action, with legacy ScopeKind as fallback.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> SupportedScopes => ScopeKinds is { Count: > 0 } ? ScopeKinds : [ScopeKind];
}

/// <summary>Optional plugin capability for semantic preset capture, instance resolution and atomic apply.</summary>
/// <remarks>
/// <paramref name="MaxActions"/> bounds the number of actions in one persisted preset. It does not
/// limit the published descriptor list: a plugin may advertise several selectable actions while a
/// host still applies one bounded action atomically.
/// </remarks>
public sealed record PresetCapability(
    string SchemaVersion,
    string? GameBuild,
    IReadOnlyList<PresetActionCapability> Actions,
    int MaxActions = 32,
    string? FormatVariant = null,
    string? ReferencePackIdentity = null,
    string PresetKind = "toolkit.action-parameters",
    string? SchemaFingerprint = null,
    string? FileRole = null);

/// <summary>Outcome of matching a persisted semantic instance reference.</summary>
public enum PresetInstanceResolutionStatus
{
    /// <summary>Exactly one eligible current instance matched.</summary>
    Resolved,
    /// <summary>No current instance matched.</summary>
    Missing,
    /// <summary>More than one current instance matched.</summary>
    Ambiguous,
    /// <summary>The document cannot resolve this instance kind.</summary>
    Unsupported,
}

/// <summary>Resolution result for one persisted instance reference; never guesses on ambiguity.</summary>
public sealed record PresetInstanceResolution(
    PresetInstanceReference Requested,
    PresetInstanceResolutionStatus Status,
    IReadOnlyList<PresetInstanceReference> Candidates = null!,
    string? Message = null);

/// <summary>Explicit user choice for an ambiguous semantic instance.</summary>
public sealed record PresetInstanceChoice(string RequestedStableId, string CandidateStableId);

/// <summary>Pure resolution output for a preset before any mutation is attempted.</summary>
public sealed record PresetResolution(IReadOnlyList<PresetInstanceResolution> Instances)
{
    /// <summary>True only when every requested instance resolved exactly once.</summary>
    public bool Succeeded => Instances.All(item => item.Status == PresetInstanceResolutionStatus.Resolved);
}

/// <summary>
/// Opt-in plugin contract for presets. Implementations must be stateless and must not perform I/O;
/// host storage and revision/undo boundaries remain framework responsibilities.
/// </summary>
public interface IPresetDocument
{
    /// <summary>Schema, build and action capabilities for this document.</summary>
    PresetCapability PresetCapability { get; }

    /// <summary>Normalizes a host capture request into semantic targets without performing I/O.</summary>
    PresetAction CapturePreset(PresetCaptureRequest request) =>
        new(request.ActionId, request.Parameters, [], request.ScopeKind);

    /// <summary>Resolves stable semantic instance ids against the current document without mutation.</summary>
    PresetResolution ResolvePresetInstances(IReadOnlyList<PresetInstanceReference> instances);

    /// <summary>Resolves with explicit candidate choices; the default preserves older adapters.</summary>
    PresetResolution ResolvePresetInstances(IReadOnlyList<PresetInstanceReference> instances,
        IReadOnlyDictionary<string, string> choices) => ResolvePresetInstances(instances);

    /// <summary>Builds a deterministic F6-style plan without changing the document.</summary>
    ActionPlan PlanPreset(IReadOnlyList<PresetAction> actions, PresetResolution resolution);

    /// <summary>Applies a previously reviewed plan atomically; the framework records one undo unit.</summary>
    EditOutcome ApplyPreset(IReadOnlyList<PresetAction> actions, PresetResolution resolution, ActionPlan plan);
}
