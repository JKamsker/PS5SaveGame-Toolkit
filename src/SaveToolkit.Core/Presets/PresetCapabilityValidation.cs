using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Presets;

internal static class PresetCapabilityValidation
{
    public static string? Validate(PresetCapability? capability)
    {
        if (capability is null) return "The document has no published preset capability.";
        if (string.IsNullOrWhiteSpace(capability.SchemaVersion) || capability.Actions is null
            || capability.Actions.Count is < 1 or > PresetContractValidation.MaxCapabilityActions
            || capability.MaxActions is < 1 or > 32
            || capability.SchemaVersion.Length > PresetContractValidation.MaxMetadataLength
            || capability.GameBuild is { Length: > PresetContractValidation.MaxMetadataLength }
            || capability.FormatVariant is { Length: > PresetContractValidation.MaxMetadataLength }
            || capability.ReferencePackIdentity is { Length: > PresetContractValidation.MaxMetadataLength }
            || capability.PresetKind is { Length: > PresetContractValidation.MaxMetadataLength }
            || capability.SchemaFingerprint is { Length: > PresetContractValidation.MaxMetadataLength }
            || capability.FileRole is { Length: > PresetContractValidation.MaxMetadataLength }) return "The preset capability metadata is malformed.";
        foreach (var action in capability.Actions)
            if (ValidateAction(action) is { } error) return error;
        if (capability.Actions.Select(action => action.ActionId).Distinct(StringComparer.Ordinal).Count() != capability.Actions.Count)
            return "The preset capability contains duplicate action identifiers.";
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(capability.Actions).Length > PresetContractValidation.MaxCapabilityBytes
                ? "The preset capability metadata exceeds the bounded size." : null;
        }
        catch (JsonException) { return "The preset capability metadata is not serializable."; }
    }

    private static string? ValidateAction(PresetActionCapability? action)
    {
        if (action is null || string.IsNullOrWhiteSpace(action.ActionId) || action.ActionId.Length > PresetContractValidation.MaxKeyLength
            || string.IsNullOrWhiteSpace(action.Label) || action.Label.Length > PresetContractValidation.MaxMetadataLength
            || action.ParameterIds is null || action.ParameterIds.Count > PresetContractValidation.MaxMapEntries
            || action.ParameterIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > PresetContractValidation.MaxKeyLength)
            || action.ParameterIds.Distinct(StringComparer.Ordinal).Count() != action.ParameterIds.Count
            || action.SupportedScopes.Count is < 1 or > PresetContractValidation.MaxScopeKinds
            || action.SupportedScopes.Any(scope => !PresetContractValidation.IsSupportedScope(scope))
            || action.SupportedScopes.Distinct(StringComparer.Ordinal).Count() != action.SupportedScopes.Count)
            return "The preset capability metadata is malformed.";
        if (action.ParameterSpecs is not null && (action.ParameterSpecs.Count != action.ParameterIds.Count
                || action.ParameterSpecs.Any(spec => spec is null))) return "The preset capability metadata is malformed.";
        if (action.ParameterIds.Count > 0 && action.ParameterSpecs is null) return "The preset capability metadata is malformed.";
        foreach (var spec in action.ParameterSpecs ?? [])
            if (ValidateFieldSpec(spec) is not null) return "The preset capability metadata is malformed.";
        return null;
    }

    private static string? ValidateFieldSpec(FieldSpec? spec)
    {
        if (spec is null || spec.Unit is { Length: > PresetContractValidation.MaxMetadataLength }) return "malformed";
        return spec switch
        {
            EnumSpec item when item.Options is null || item.Options.Count > PresetContractValidation.MaxMapEntries
                || item.Options.Any(option => option is null || string.IsNullOrWhiteSpace(option.Value)
                    || option.Value.Length > PresetContractValidation.MaxKeyLength || string.IsNullOrWhiteSpace(option.Label)
                    || option.Label.Length > PresetContractValidation.MaxMetadataLength || option.Description is { Length: > PresetContractValidation.MaxMetadataLength })
                || item.Options.Select(option => option.Value).Distinct(StringComparer.Ordinal).Count() != item.Options.Count => "malformed",
            FlagsSpec item when item.Bits is null || item.Bits.Count > 64
                || item.Bits.Any(bit => bit is null || string.IsNullOrWhiteSpace(bit.Label) || bit.Label.Length > PresetContractValidation.MaxMetadataLength) => "malformed",
            NullableSpec item => ValidateFieldSpec(item.Inner),
            UnionSpec item when item.Options is null || item.Options.Count is < 1 or > 32
                || item.Options.Any(option => option is null || string.IsNullOrWhiteSpace(option.Id)
                    || option.Id.Length > PresetContractValidation.MaxKeyLength || string.IsNullOrWhiteSpace(option.Label)
                    || option.Label.Length > PresetContractValidation.MaxMetadataLength || ValidateFieldSpec(option.Spec) is not null)
                || item.Options.Select(option => option.Id).Distinct(StringComparer.Ordinal).Count() != item.Options.Count => "malformed",
            ReferenceSpec item when string.IsNullOrWhiteSpace(item.CatalogId) || item.CatalogId.Length > PresetContractValidation.MaxKeyLength => "malformed",
            TextSpec item when item.MaxLength is < 0 or > PresetContractValidation.MaxRequestBytes => "malformed",
            ExactNumberSpec item when item.MaxLength is < 1 or > PresetContractValidation.MaxRequestBytes
                || item.MaxAbsoluteExponent is < 0 or > PresetContractValidation.MaxRequestBytes
                || item.Min is { Length: > PresetContractValidation.MaxMetadataLength } || item.Max is { Length: > PresetContractValidation.MaxMetadataLength } => "malformed",
            _ => null,
        };
    }
}
