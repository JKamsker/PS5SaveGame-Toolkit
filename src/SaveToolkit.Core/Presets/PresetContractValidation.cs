using System.Text.Json;
using SaveToolkit.Core.Editing;
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

    public static bool HasSemanticBoundaries(PresetCompatibility compatibility) =>
        compatibility.GameBuild is not null || compatibility.FormatVariant is not null
        || compatibility.ReferencePackIdentity is not null || compatibility.SchemaFingerprint is not null
        || compatibility.FileRole is not null || compatibility.CapabilitySchemaVersion is not null
        || !string.Equals(compatibility.PresetKind, "toolkit.action-parameters", StringComparison.Ordinal);

    public static string? ValidateCapture(PresetCapability? capability, PresetCaptureRequest request)
    {
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
        var action = capability.Actions.FirstOrDefault(item => item.ActionId == request.ActionId);
        return action is null ? "The action is not published by this preset capability." : ValidateParameters(action, request.Parameters, request.ScopeKind);
    }

    public static string? ValidateCapturedAction(PresetCapability? capability, PresetCaptureRequest request, PresetAction action)
    {
        if (!string.Equals(action.ActionId, request.ActionId, StringComparison.Ordinal)) return "The adapter returned a different action.";
        if (!string.Equals(action.ScopeKind, request.ScopeKind, StringComparison.Ordinal)) return "The adapter changed the requested scope.";
        var contract = capability?.Actions.FirstOrDefault(item => item.ActionId == action.ActionId);
        if (capability is not null && contract is null) return "The adapter returned an unpublished action.";
        var parameterError = contract is null ? null : ValidateParameters(contract, action.Parameters, action.ScopeKind);
        if (parameterError is not null) return parameterError;
        if (action.Instances is { Count: > MaxSelectedRows }) return "Preset instance targets exceed the bounded limit.";
        if (action.Instances is { } instances && instances.Any(instance => string.IsNullOrWhiteSpace(instance.Kind)
            || string.IsNullOrWhiteSpace(instance.StableId) || instance.StableId.Length > MaxKeyLength))
            return "Preset instance targets must use bounded stable identifiers.";
        if (action.Instances is { } refs && refs.Select(reference => $"{reference.Kind}\0{reference.StableId}").Distinct(StringComparer.Ordinal).Count() != refs.Count)
            return "Preset instance targets must be distinct.";
        return null;
    }

    public static string? ValidateActions(PresetCapability capability, IReadOnlyList<PresetAction> actions)
    {
        if (actions.Count is < 1 or > 32 || actions.Count > capability.MaxActions) return "The preset action count is unsupported.";
        foreach (var action in actions)
        {
            var contract = capability.Actions.FirstOrDefault(item => item.ActionId == action.ActionId);
            if (contract is null) return $"Action '{action.ActionId}' is not published by this capability.";
            if (ValidateParameters(contract, action.Parameters, action.ScopeKind) is { } error) return error;
            if (action.Instances is { } refs && refs.Any(reference => string.IsNullOrWhiteSpace(reference.StableId)))
                return "Preset instance targets must use stable identifiers.";
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

    private static string? ValidateParameters(PresetActionCapability capability, IReadOnlyDictionary<string, JsonElement> parameters, string scope)
    {
        if (!capability.SupportedScopes.Contains(scope, StringComparer.Ordinal)) return "The action does not support this preset scope.";
        if (parameters.Count != capability.ParameterIds.Count || parameters.Keys.Any(key => !capability.ParameterIds.Contains(key, StringComparer.Ordinal)))
            return "Preset parameters do not match the published action contract.";
        if (capability.ParameterSpecs is not { Count: > 0 }) return null;
        if (capability.ParameterSpecs.Count != capability.ParameterIds.Count) return "The action capability has invalid parameter metadata.";
        for (var index = 0; index < capability.ParameterIds.Count; index++)
        {
            var id = capability.ParameterIds[index];
            var result = FieldValueCoercer.FromJson(parameters[id], capability.ParameterSpecs[index]);
            if (!result.Ok) return $"Parameter '{id}' is invalid: {result.Error}";
        }
        return null;
    }
}
