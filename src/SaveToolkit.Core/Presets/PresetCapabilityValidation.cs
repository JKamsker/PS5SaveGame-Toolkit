using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Presets;

internal static class PresetCapabilityValidation
{
    private const string MalformedReason = "Preset capability metadata is malformed.";

    public static string? Validate(PresetCapability? capability) => TryDetach(capability, out _, out var error) ? null : error;

    public static bool TryDetach(PresetCapability? capability, out PresetCapability? detached, out string? error)
    {
        detached = null;
        error = null;
        try
        {
            if (capability is null)
                return Fail("The document has no published preset capability.", out error);
            if (string.IsNullOrWhiteSpace(capability.SchemaVersion) || capability.Actions is null
                || capability.Actions.Count is < 1 or > PresetContractValidation.MaxCapabilityActions
                || capability.MaxActions is < 1 or > 32
                || TooLong(capability.SchemaVersion) || TooLong(capability.GameBuild)
                || TooLong(capability.FormatVariant) || TooLong(capability.ReferencePackIdentity)
                || TooLong(capability.PresetKind) || TooLong(capability.SchemaFingerprint)
                || TooLong(capability.FileRole))
                return Fail("The preset capability metadata is malformed.", out error);

            var actions = new List<PresetActionCapability>(capability.Actions.Count);
            foreach (var action in capability.Actions)
            {
                if (!TryDetachAction(action, out var detachedAction, out error)) return false;
                actions.Add(detachedAction!);
            }
            if (actions.Select(action => action.ActionId).Distinct(StringComparer.Ordinal).Count() != actions.Count)
                return Fail("The preset capability contains duplicate action identifiers.", out error);
            var snapshot = capability with { Actions = Array.AsReadOnly(actions.ToArray()) };
            if (JsonSerializer.SerializeToUtf8Bytes(snapshot.Actions).Length > PresetContractValidation.MaxCapabilityBytes)
                return Fail("The preset capability metadata exceeds the bounded size.", out error);
            detached = snapshot;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            return Fail(MalformedReason, out error);
        }
    }

    private static bool TryDetachAction(PresetActionCapability? action, out PresetActionCapability? detached, out string? error)
    {
        detached = null;
        error = null;
        if (action is null || string.IsNullOrWhiteSpace(action.ActionId) || TooLong(action.ActionId)
            || string.IsNullOrWhiteSpace(action.Label) || TooLong(action.Label)
            || action.ParameterIds is null || action.ParameterIds.Count > PresetContractValidation.MaxMapEntries
            || action.ParameterIds.Any(id => string.IsNullOrWhiteSpace(id) || TooLong(id))
            || action.ParameterIds.Distinct(StringComparer.Ordinal).Count() != action.ParameterIds.Count
            || TooLong(action.ScopeKind))
            return Fail("The preset capability metadata is malformed.", out error);

        var scopes = action.ScopeKinds is { Count: > 0 } ? action.ScopeKinds : [action.ScopeKind];
        if (scopes.Count is < 1 or > PresetContractValidation.MaxScopeKinds
            || scopes.Any(scope => !PresetContractValidation.IsSupportedScope(scope) || TooLong(scope))
            || scopes.Distinct(StringComparer.Ordinal).Count() != scopes.Count)
            return Fail("The preset capability metadata is malformed.", out error);
        if (action.ParameterSpecs is not null && (action.ParameterSpecs.Count != action.ParameterIds.Count
                || action.ParameterSpecs.Any(spec => spec is null)))
            return Fail("The preset capability metadata is malformed.", out error);
        if (action.ParameterIds.Count > 0 && action.ParameterSpecs is null)
            return Fail("The preset capability metadata is malformed.", out error);

        var active = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var nodes = 0;
        var sourceSpecs = action.ParameterSpecs;
        var specs = sourceSpecs is null ? null : new FieldSpec[sourceSpecs.Count];
        if (sourceSpecs is not null)
            for (var index = 0; index < sourceSpecs.Count; index++)
            {
                if (!TryDetachSpec(sourceSpecs[index]!, 0, active, ref nodes, out var detachedSpec, out error)) return false;
                specs![index] = detachedSpec!;
            }
        detached = new PresetActionCapability(action.ActionId, action.Label, Array.AsReadOnly(action.ParameterIds.ToArray()),
            action.SupportsCapture, action.SupportsApply, specs is null ? null! : Array.AsReadOnly(specs),
            action.ScopeKind, action.ScopeKinds is null ? null! : Array.AsReadOnly(action.ScopeKinds.ToArray()));
        return true;
    }

    private static bool TryDetachSpec(FieldSpec spec, int depth, HashSet<object> active, ref int nodes,
        out FieldSpec? detached, out string? error)
    {
        detached = null;
        error = null;
        if (spec is null || depth > PresetContractValidation.MaxCapabilityDepth
            || ++nodes > PresetContractValidation.MaxCapabilityNodes || !active.Add(spec))
            return Fail("The preset capability field graph is too deep, cyclic or large.", out error);
        try
        {
            if (TooLong(spec.Unit)) return Fail("The preset parameter specification is malformed.", out error);
            switch (spec)
            {
                case EnumSpec item:
                    if (item.Options is null || item.Options.Count > PresetContractValidation.MaxMapEntries
                        || item.Options.Any(option => option is null || string.IsNullOrWhiteSpace(option.Value)
                            || TooLong(option.Value) || string.IsNullOrWhiteSpace(option.Label) || TooLong(option.Label)
                            || TooLong(option.Description))
                        || item.Options.Select(option => option.Value).Distinct(StringComparer.Ordinal).Count() != item.Options.Count)
                        return Fail("The preset parameter specification is malformed.", out error);
                    detached = item with { Options = Array.AsReadOnly(item.Options.Select(option => option with { }).ToArray()) };
                    break;
                case FlagsSpec item:
                    if (item.BitWidth is < 0 or > PresetContractValidation.MaxFlagWidth
                        || item.Bits is null || item.Bits.Count > PresetContractValidation.MaxFlagBits
                        || item.Bits.Any(bit => bit is null || bit.Bit < 0 || bit.Bit >= item.BitWidth
                            || string.IsNullOrWhiteSpace(bit.Label) || TooLong(bit.Label) || TooLong(bit.Description))
                        || item.Bits.Select(bit => bit.Bit).Distinct().Count() != item.Bits.Count)
                        return Fail("The preset parameter flags specification is malformed.", out error);
                    detached = item with { Bits = Array.AsReadOnly(item.Bits.Select(bit => bit with { }).ToArray()) };
                    break;
                case NullableSpec item:
                    if (!TryDetachSpec(item.Inner, depth + 1, active, ref nodes, out var inner, out error)) return false;
                    detached = item with { Inner = inner! };
                    break;
                case UnionSpec item:
                    if (item.Options is null || item.Options.Count is < 1 or > PresetContractValidation.MaxUnionOptions)
                        return Fail("The preset union specification is malformed.", out error);
                    var options = new UnionOption[item.Options.Count];
                    var optionIds = new HashSet<string>(StringComparer.Ordinal);
                    for (var index = 0; index < item.Options.Count; index++)
                    {
                        var option = item.Options[index];
                        if (option is null || string.IsNullOrWhiteSpace(option.Id) || TooLong(option.Id)
                            || string.IsNullOrWhiteSpace(option.Label) || TooLong(option.Label)
                            || !optionIds.Add(option.Id)
                            || !TryDetachSpec(option.Spec, depth + 1, active, ref nodes, out var optionSpec, out error)) return false;
                        options[index] = option with { Spec = optionSpec! };
                    }
                    detached = item with { Options = Array.AsReadOnly(options) };
                    break;
                case ReferenceSpec item when string.IsNullOrWhiteSpace(item.CatalogId) || TooLong(item.CatalogId):
                    return Fail("The preset reference specification is malformed.", out error);
                case TextSpec item when item.MaxLength is < 0 or > PresetContractValidation.MaxRequestBytes:
                    return Fail("The preset text specification is malformed.", out error);
                case ExactNumberSpec item when ExactJsonNumber.ValidateSpec(item) is not null
                    || TooLong(item.Min) || TooLong(item.Max):
                    return Fail("The preset number specification is malformed.", out error);
                case IntegerSpec item when item.Min > item.Max:
                    return Fail("The preset integer specification is malformed.", out error);
                case FloatSpec item when !double.IsFinite(item.UnitsPerSecond) || item.UnitsPerSecond <= 0
                    || item.Min is double min && !double.IsFinite(min) || item.Max is double max && !double.IsFinite(max)
                    || item.Min is double lower && item.Max is double upper && lower > upper:
                    return Fail("The preset float specification is malformed.", out error);
                default:
                    detached = spec switch
                    {
                        BoolSpec item => item with { },
                        IntegerSpec item => item with { },
                        FloatSpec item => item with { },
                        TextSpec item => item with { },
                        DateTimeSpec item => item with { },
                        ExactNumberSpec item => item with { },
                        ReferenceSpec item => item with { },
                        _ => null,
                    };
                    if (detached is null) return Fail("The preset parameter specification is unsupported.", out error);
                    break;
            }
            return true;
        }
        finally { active.Remove(spec); }
    }

    private static bool TooLong(string? value) => value is { Length: > PresetContractValidation.MaxMetadataLength };

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }
}
