using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Presets;

/// <summary>Shared host validation for semantic preset capture, planning and bounded maps.</summary>
public static class PresetContractValidation
{
    public const int MaxMapEntries = 128;
    public const int MaxKeyLength = 256;
    public const int MaxSelectedRows = 5000;
    public const int MaxRequestBytes = 256 * 1024;
    public const int MaxSemanticInstances = 256;
    public const int MaxResolutionCandidates = 128;
    public const int MaxResolutionBytes = 256 * 1024;
    public const int MaxMetadataLength = 512;

    public static bool HasSemanticBoundaries(PresetCompatibility compatibility) =>
        compatibility.GameBuild is not null || compatibility.FormatVariant is not null
        || compatibility.ReferencePackIdentity is not null || compatibility.SchemaFingerprint is not null
        || compatibility.FileRole is not null || compatibility.CapabilitySchemaVersion is not null
        || !string.Equals(compatibility.PresetKind, "toolkit.action-parameters", StringComparison.Ordinal);

    public static string? ValidateCapture(PresetCapability? capability, PresetCaptureRequest? request, ReferenceFieldService? references = null)
    {
        if (request is null) return "The capture request is malformed.";
        if (!PresetScopeKinds.AllEligible.Equals(request.ScopeKind, StringComparison.Ordinal)
            && !PresetScopeKinds.AllCopies.Equals(request.ScopeKind, StringComparison.Ordinal)
            && !PresetScopeKinds.Document.Equals(request.ScopeKind, StringComparison.Ordinal)
            && !PresetScopeKinds.Selected.Equals(request.ScopeKind, StringComparison.Ordinal))
            return "The capture scope is not supported.";
        if (request.Parameters.Count > MaxMapEntries) return "Preset parameters exceed the bounded limit.";
        if (request.Parameters.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > MaxKeyLength))
            return "Preset parameter keys are invalid.";
        if (JsonSerializer.SerializeToUtf8Bytes(request.Parameters).Length > MaxRequestBytes)
            return "Preset parameters exceed the bounded request size.";
        if (request.SelectedRowKeys is { } keys)
        {
            if (keys.Count > MaxSelectedRows || keys.Any(key => string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength)
                || keys.Distinct(StringComparer.Ordinal).Count() != keys.Count)
                return "Selected row keys must be bounded, nonempty and distinct.";
        }
        if (request.AllMatches && request.ScopeKind is not (PresetScopeKinds.AllEligible or PresetScopeKinds.AllCopies))
            return "allMatches is only valid for allEligible or allCopies capture.";
        if (request.ScopeKind == PresetScopeKinds.Selected && request.SelectedRowKeys is null)
            return "Selected capture must provide an explicit row-key list.";
        if (capability is null)
            return request.ScopeKind == PresetScopeKinds.Document
                || request.ScopeKind == PresetScopeKinds.Selected && request.SelectedRowKeys is { Count: 0 }
                ? null
                : "The document has no semantic preset capability for this scope.";
        if (ValidateCapability(capability) is { } capabilityError) return capabilityError;
        var action = capability.Actions.FirstOrDefault(item => item.ActionId == request.ActionId);
        if (action is null) return "The action is not published by this preset capability.";
        if (!action.SupportsCapture) return "This action does not support preset capture.";
        return ValidateParameters(action, request.Parameters, request.ScopeKind, references);
    }

    public static string? ValidateCapturedAction(PresetCapability? capability, PresetCaptureRequest? request, PresetAction? action, ReferenceFieldService? references = null)
    {
        if (request is null) return "The capture request is malformed.";
        if (action is null) return "The capture adapter returned no action.";
        if (!string.Equals(action.ActionId, request.ActionId, StringComparison.Ordinal)) return "The adapter returned a different action.";
        if (!string.Equals(action.ScopeKind, request.ScopeKind, StringComparison.Ordinal)) return "The adapter changed the requested scope.";
        if (capability is not null && ValidateCapability(capability) is { } capabilityError) return capabilityError;
        var contract = capability?.Actions.FirstOrDefault(item => item.ActionId == action.ActionId);
        if (capability is not null && contract is null) return "The adapter returned an unpublished action.";
        if (contract is not null && !contract.SupportsCapture) return "The action does not support preset capture.";
        var parameterError = contract is null ? null : ValidateParameters(contract, action.Parameters, action.ScopeKind, references);
        if (parameterError is not null) return parameterError;
        return ValidateInstances(action.Instances);
    }

    public static string? ValidateActions(PresetCapability? capability, IReadOnlyList<PresetAction>? actions, ReferenceFieldService? references = null)
    {
        if (capability is null || actions is null) return "The preset contract is malformed.";
        if (ValidateCapability(capability) is { } capabilityError) return capabilityError;
        if (actions.Count is < 1 or > 32 || actions.Count > capability.MaxActions) return "The preset action count is unsupported.";
        if (ValidateAggregateInstances(actions) is { } aggregateError) return aggregateError;
        foreach (var action in actions)
        {
            var contract = capability.Actions.FirstOrDefault(item => item.ActionId == action.ActionId);
            if (contract is null) return $"Action '{action.ActionId}' is not published by this capability.";
            if (!contract.SupportsApply) return $"Action '{action.ActionId}' does not support preset apply.";
            if (ValidateParameters(contract, action.Parameters, action.ScopeKind, references) is { } error) return error;
            if (ValidateInstances(action.Instances) is { } instanceError) return instanceError;
        }
        return null;
    }

    public static string? ValidateCapability(PresetCapability? capability)
    {
        if (capability is null) return "The document has no published preset capability.";
        if (string.IsNullOrWhiteSpace(capability.SchemaVersion) || capability.Actions is null
            || capability.MaxActions is < 1 or > 32) return "The preset capability metadata is malformed.";
        if (capability.Actions.Any(action => action is null || string.IsNullOrWhiteSpace(action.ActionId)
            || action.ParameterIds is null || action.ParameterIds.Count > MaxMapEntries
            || action.ParameterIds.Distinct(StringComparer.Ordinal).Count() != action.ParameterIds.Count
            || action.SupportedScopes is null || action.SupportedScopes.Count == 0
            || action.SupportedScopes.Any(scope => !IsScope(scope))
            || action.ParameterSpecs is not null && (action.ParameterSpecs.Count != action.ParameterIds.Count
                || action.ParameterSpecs.Any(spec => spec is null))
            || action.ParameterIds.Count > 0 && action.ParameterSpecs is null)) return "The preset capability metadata is malformed.";
        if (capability.Actions.Select(action => action.ActionId).Distinct(StringComparer.Ordinal).Count() != capability.Actions.Count)
            return "The preset capability contains duplicate action identifiers.";
        return null;
    }

    public static string? ValidateResolution(PresetResolution? resolution, out byte[]? serialized)
    {
        serialized = null;
        if (resolution?.Instances is null || resolution.Instances.Count > MaxSemanticInstances)
            return "Preset resolution exceeds the bounded instance limit.";
        foreach (var item in resolution.Instances)
        {
            if (item is null || ValidateReference(item.Requested) is not null)
                return "Preset resolution contains an invalid requested target.";
            var candidates = item.Candidates ?? [];
            if (candidates.Count > MaxResolutionCandidates)
                return "Preset resolution exceeds the bounded candidate limit.";
            if (candidates.Any(candidate => ValidateReference(candidate) is not null))
                return "Preset resolution contains invalid candidate metadata.";
            if (candidates.Select(candidate => $"{candidate.Kind}\0{candidate.StableId}").Distinct(StringComparer.Ordinal).Count() != candidates.Count)
                return "Preset resolution contains duplicate candidates.";
            if (item.Message is { Length: > MaxMetadataLength }) return "Preset resolution messages exceed the bounded limit.";
        }
        try
        {
            serialized = JsonSerializer.SerializeToUtf8Bytes(resolution);
            return serialized.Length > MaxResolutionBytes ? "Preset resolution exceeds the bounded serialized limit." : null;
        }
        catch (JsonException) { return "Preset resolution is not serializable."; }
    }

    public static string? ValidateInstanceReferences(IReadOnlyList<PresetInstanceReference>? instances) => ValidateInstances(instances);

    public static string? ValidateAggregateInstances(IReadOnlyList<PresetAction>? actions)
    {
        if (actions is null) return "Preset actions are missing.";
        var total = 0;
        foreach (var action in actions)
        {
            if (action is null) return "Preset actions contain a null action.";
            total = checked(total + (action.Instances?.Count ?? 0));
            if (total > MaxSemanticInstances) return "Preset instance targets exceed the aggregate bounded limit.";
        }
        return null;
    }

    public static string? ValidateChoices(IReadOnlyDictionary<string, string>? choices)
    {
        if (choices is null) return null;
        if (choices.Count > MaxMapEntries || choices.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > MaxKeyLength
            || string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Length > MaxKeyLength))
            return "Preset choices exceed the bounded limit.";
        return JsonSerializer.SerializeToUtf8Bytes(choices).Length > MaxRequestBytes ? "Preset choices exceed the bounded request size." : null;
    }

    private static string? ValidateParameters(PresetActionCapability capability, IReadOnlyDictionary<string, JsonElement>? parameters, string scope, ReferenceFieldService? references)
    {
        if (parameters is null) return "Preset parameters are missing.";
        if (!capability.SupportedScopes.Contains(scope, StringComparer.Ordinal)) return "The action does not support this preset scope.";
        if (parameters.Count != capability.ParameterIds.Count || parameters.Keys.Any(key => !capability.ParameterIds.Contains(key, StringComparer.Ordinal)))
            return "Preset parameters do not match the published action contract.";
        if (capability.ParameterSpecs is not { Count: > 0 }) return null;
        if (capability.ParameterSpecs.Count != capability.ParameterIds.Count) return "The action capability has invalid parameter metadata.";
        for (var index = 0; index < capability.ParameterIds.Count; index++)
        {
            var id = capability.ParameterIds[index];
            var result = FieldValueCoercer.FromJson(parameters[id], capability.ParameterSpecs[index], references);
            if (!result.Ok) return $"Parameter '{id}' is invalid: {result.Error}";
        }
        return null;
    }

    private static string? ValidateInstances(IReadOnlyList<PresetInstanceReference>? instances)
    {
        if (instances is null) return null;
        if (instances.Count > MaxSemanticInstances) return "Preset instance targets exceed the bounded limit.";
        if (instances.Any(reference => ValidateReference(reference) is not null)) return "Preset instance targets contain invalid bounded metadata.";
        return instances.Select(reference => $"{reference.Kind}\0{reference.StableId}").Distinct(StringComparer.Ordinal).Count() == instances.Count
            ? null : "Preset instance targets must be distinct.";
    }

    private static string? ValidateReference(PresetInstanceReference? reference) => reference is null
        || string.IsNullOrWhiteSpace(reference.Kind) || string.IsNullOrWhiteSpace(reference.StableId)
        || reference.Kind.Length > MaxKeyLength || reference.StableId.Length > MaxKeyLength
        || reference.Label is { Length: > MaxMetadataLength } || reference.Role is { Length: > MaxMetadataLength }
        || reference.Path is { Length: > MaxMetadataLength } || reference.Build is { Length: > MaxMetadataLength }
        || reference.Provenance is { Length: > MaxMetadataLength }
        ? "invalid reference" : null;

    private static bool IsScope(string scope) => scope is PresetScopeKinds.Document or PresetScopeKinds.AllEligible
        or PresetScopeKinds.AllCopies or PresetScopeKinds.Selected;
}
